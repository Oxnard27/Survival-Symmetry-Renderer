using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NLog;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Plugins;

#if !LOCAL_BUILD
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
#endif

namespace SurvivalSymmetryRenderer
{
    /// <summary>
    /// Optional render-only enhancement for the Survival Symmetry Workshop mod.
    ///
    /// This plugin changes no placement/input logic. It only alters the value returned
    /// by MyCubeBuilder.UseSymmetry while MyCubeBuilder.Draw() is executing so Keen's native mirrored block preview can render.
    /// </summary>
    public sealed class Plugin : IPlugin
    {
        public const string HarmonyId = "SurvivalSymmetry.Renderer";

        internal static readonly Logger Log =
            LogManager.GetLogger("SurvivalSymmetryRenderer");

        private Harmony harmony;
        private bool updateLogged;

        public void Init(object gameInstance)
        {
            Log.Info("[SurvivalSymmetry Renderer] Init entered");

            RendererState.Resolve();
            Log.Info("[SurvivalSymmetry Renderer] Reflection: " + RendererState.Describe());

            harmony = new Harmony(HarmonyId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            Log.Info("[SurvivalSymmetry Renderer] Harmony patches applied");
        }

        public void Update()
        {
            if (!updateLogged)
            {
                updateLogged = true;
                Log.Info("[SurvivalSymmetry Renderer] Update entered");
            }
        }

        public void Dispose()
        {
            Log.Info("[SurvivalSymmetry Renderer] Dispose entered");

            if (harmony != null)
                harmony.UnpatchAll(HarmonyId);

            harmony = null;
            RendererState.DrawDepth = 0;
        }
    }

    internal static class RendererState
    {
        [ThreadStatic]
        internal static int DrawDepth;

        internal static FieldInfo UseSymmetryField;
        internal static PropertyInfo UseSymmetryProperty;

        internal static bool DrawActive
        {
            get { return DrawDepth > 0; }
        }

        internal static void Resolve()
        {
            Type t = typeof(MyCubeBuilder);

            // Runtime reflection avoids the earlier compile-time access failure.
            UseSymmetryField = AccessTools.Field(t, "m_useSymmetry");
            UseSymmetryProperty = AccessTools.Property(t, "UseSymmetry");
        }

        internal static string Describe()
        {
            return "m_useSymmetry=" + (UseSymmetryField != null) +
                   ", UseSymmetryProperty=" + (UseSymmetryProperty != null);
        }

        internal static bool BuilderWantsSymmetry(MyCubeBuilder builder)
        {
            if (builder == null || UseSymmetryField == null)
                return false;

            try
            {
                object value = UseSymmetryField.GetValue(builder);
                return value is bool && (bool)value;
            }
            catch (Exception e)
            {
                Plugin.Log.Debug(e,
                    "[SurvivalSymmetry Renderer] Could not read m_useSymmetry");
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(MyCubeBuilder), "Draw")]
    internal static class CubeBuilderDrawPatch
    {
        private static bool logged;

        [HarmonyPrefix]
        private static void Prefix()
        {
            RendererState.DrawDepth++;

            if (!logged)
            {
                logged = true;
                Plugin.Log.Info("[SurvivalSymmetry Renderer] MyCubeBuilder.Draw reached");
            }
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception)
        {
            if (RendererState.DrawDepth > 0)
                RendererState.DrawDepth--;

            return __exception;
        }
    }

    /// <summary>
    /// Patch the actual UseSymmetry getter if it exists. Outside Draw this is a no-op,
    /// so vanilla placement/input cannot acquire symmetry from this plugin.
    /// </summary>
    [HarmonyPatch]
    internal static class UseSymmetryGetterPatch
    {
        private static bool logged;

        private static MethodBase TargetMethod()
        {
            PropertyInfo property = AccessTools.Property(
                typeof(MyCubeBuilder), "UseSymmetry");

            return property != null ? property.GetGetMethod(true) : null;
        }

        [HarmonyPostfix]
        private static void Postfix(MyCubeBuilder __instance, ref bool __result)
        {
            if (!RendererState.DrawActive)
                return;

            // Respect the CubeBuilder's own backing state. The Workshop mod already
            // synchronizes this state; This renderer only bypasses Survival's getter gate
            // during rendering.
            if (!RendererState.BuilderWantsSymmetry(__instance))
                return;

            __result = true;

            if (!logged)
            {
                logged = true;
                Plugin.Log.Info(
                    "[SurvivalSymmetry Renderer] UseSymmetry forced TRUE during Draw");
            }
        }
    }

    /// <summary>
    /// Tracks whether Keen's own block-model staging function is
    /// reached after the Draw-only UseSymmetry override.
    /// </summary>
    [HarmonyPatch]
    internal static class AddFastBuildModelsPatch
    {
        private static bool logged;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            MethodInfo[] methods = typeof(MyCubeBuilder).GetMethods(
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic);

            for (int i = 0; i < methods.Length; i++)
            {
                if (String.Equals(
                    methods[i].Name,
                    "AddFastBuildModels",
                    StringComparison.Ordinal))
                {
                    yield return methods[i];
                }
            }
        }

        [HarmonyPrefix]
        private static void Prefix()
        {
            if (!RendererState.DrawActive || logged)
                return;

            logged = true;
            Plugin.Log.Info(
                "[SurvivalSymmetry Renderer] AddFastBuildModels reached during Draw");
        }
    }
}
