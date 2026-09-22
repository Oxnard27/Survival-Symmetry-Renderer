# Survival Symmetry Renderer

Optional client-side Pulsar plugin for the **Survival Symmetry** Space Engineers Workshop mod.

## What it does

Survival Symmetry already works without this plugin and provides its own cyan/green/red placement outlines.

This optional renderer lets Keen's native CubeBuilder render the **actual mirrored block model** at valid mirrored placement positions.

The plugin is intentionally **render-only**. It does not handle block placement, materials, drag placement, welding, grinding, painting, networking, or server authority.

## Requirements

- Space Engineers 1
- Pulsar
- Survival Symmetry Workshop mod

## Multiplayer

This is a client-side visual enhancement. Dedicated servers do **not** need to install it.

## Installation through Pulsar

Once accepted into Pulsar PluginHub, enable **Survival Symmetry Renderer** in Pulsar's plugin list.

## Development / testing

The plugin has been tested with Pulsar Interim/CoreCLR source loading. For local development, configure your Space Engineers `Bin64` path in a local `Directory.Build.props` (ignored by Git), or use the current Pulsar client-plugin development workflow.

## Bug reports

Please report renderer-specific problems through this repository's GitHub Issues page. For Survival Symmetry Workshop-mod gameplay issues, use the Workshop mod's normal bug-report channel.

## AI-assisted development

AI-assisted development was used during development and testing.
