# Food4Rhino Listing Draft

## Title

RhinoWFS

## Short Description

Grasshopper plugin for Rhino 8 that loads WFS data directly into Grasshopper.

## Long Description

RhinoWFS is a Grasshopper plugin for Rhino 8 that tries to keep WFS loading simple and practical.

Many municipalities and public agencies publish planning and GIS data through WFS services. RhinoWFS is meant to make that data directly usable inside Grasshopper without requiring a separate GIS workflow first.

Current workflow:

1. Connect a WFS URL
2. List available layers
3. Choose one or more layers
4. Optionally define a bounding box
5. Load the geometry into Grasshopper

Current components:

- `WFS Layers`
- `WFS Bounding Box`
- `Load WFS`

Current focus:

- user-provided WFS URLs
- layer discovery through `GetCapabilities`
- optional bounding box filtering
- automatic SRS handling
- polygon and multipolygon support
- Grasshopper curve output

## Early Stage Note

RhinoWFS is currently still in an early alpha stage.

It has only been tested with a relatively small number of real WFS services and datasets so far. Behavior may still vary depending on the WFS provider, geometry type, coordinate system, service version, or response format.

Feedback, edge cases, and additional WFS test links are very welcome.

## Repository

https://github.com/PascalNun/RhinoWFS

## License

MIT
