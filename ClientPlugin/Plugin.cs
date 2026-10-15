using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NLog;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
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

        public void Init(object gameInstance)
        {
            Log.Info("[SurvivalSymmetry Renderer] Init entered");

            RendererState.Resolve();
            Log.Info("[SurvivalSymmetry Renderer] Reflection: " + RendererState.Describe());

            harmony = new Harmony(HarmonyId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            Log.Info("[SurvivalSymmetry Renderer] Harmony patches applied");
        }

        public void Update() { }

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
        internal const ulong WorkshopModId = 3775914515UL;
        internal static readonly AccessTools.FieldRef<MyCubeBuilder, MyCubeBuilderGizmo> Gizmo =
            AccessTools.FieldRefAccess<MyCubeBuilder, MyCubeBuilderGizmo>("m_gizmo");

        // Fail closed if the Workshop mod is not present. Checked against the
        // active session rather than a client setting or a persisted grid plane.
        internal static bool WorkshopModLoaded()
        {
            try
            {
                var session = MySession.Static;
                if (session == null)
                    return false;

                // Reflection keeps this compatible with both Pulsar source targets.
                PropertyInfo modsProperty = AccessTools.Property(session.GetType(), "Mods");
                FieldInfo modsField = AccessTools.Field(session.GetType(), "Mods");
                object mods = modsProperty != null ? modsProperty.GetValue(session, null)
                    : modsField != null ? modsField.GetValue(session) : null;
                IEnumerable entries = mods as IEnumerable;
                if (entries == null)
                    return false;

                foreach (object mod in entries)
                {
                    if (mod == null)
                        continue;
                    PropertyInfo idProperty = AccessTools.Property(mod.GetType(), "PublishedFileId");
                    FieldInfo idField = AccessTools.Field(mod.GetType(), "PublishedFileId");
                    object id = idProperty != null ? idProperty.GetValue(mod, null)
                        : idField != null ? idField.GetValue(mod) : null;
                    if (id != null && String.Equals(Convert.ToString(id),
                        WorkshopModId.ToString(), StringComparison.Ordinal))
                        return true;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.Debug(e, "[SurvivalSymmetry Renderer] Mod detection failed");
            }
            return false;
        }

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
            if (!RendererState.WorkshopModLoaded())
                return;

            RendererState.DrawDepth++;

            if (!logged)
            {
                logged = true;
                Plugin.Log.Info("[SurvivalSymmetry Renderer] MyCubeBuilder.Draw reached");
            }
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(MyCubeBuilder __instance, Exception __exception)
        {
            if (RendererState.DrawDepth > 0)
                RendererState.DrawDepth--;

            // Draw temporarily enables vanilla mirrored gizmo spaces. Their Enabled
            // state survives Draw and is used by vanilla middle-click painting.
            // Disable them once the render data has been collected, preserving
            // the rendered ghost but preventing gameplay/input side effects.
            if (RendererState.DrawDepth == 0 && __instance != null)
            {
                try
                {
                    if (!__instance.UseSymmetry)
                    {
                        MyCubeBuilderGizmo gizmo = RendererState.Gizmo(__instance);
                        if (gizmo != null)
                        {
                            foreach (var space in gizmo.Spaces)
                            {
                                if (space.SymmetryPlane != MySymmetrySettingModeEnum.Disabled)
                                    space.Enabled = false;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.Error(e, "[SurvivalSymmetry Renderer] Failed to restore gizmo state");
                }
            }
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
            if (!RendererState.DrawActive || !RendererState.WorkshopModLoaded())
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

}
