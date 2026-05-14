# Third-Party Notices

RhinoSpatial is distributed under the MIT License.

Some release packages and plugin builds also include third-party libraries as separate assemblies. Those libraries remain governed by their own licenses and notices.

This file is provided to make the packaged distribution more transparent and easier to review.

## Bundled Libraries

### BitMiracle.LibTiff.NET

- Package: `BitMiracle.LibTiff.NET`
- Version: `2.4.660`
- Project URL: https://bitmiracle.com/libtiff/
- License page: https://bitmiracle.github.io/libtiff.net/help/articles/license.html

RhinoSpatial uses BitMiracle.LibTiff.NET for GeoTIFF and TIFF raster handling.

### NetTopologySuite

- Package: `NetTopologySuite`
- Version: `2.6.0`
- Project URL: https://github.com/NetTopologySuite/NetTopologySuite
- License: `BSD-3-Clause`
- SPDX / license URL: https://licenses.nuget.org/BSD-3-Clause

RhinoSpatial uses NetTopologySuite for 2D geometry operations such as buffering, polygon union, and cleanup.

### NetTopologySuite Features

- Package: `NetTopologySuite.Features`
- Version: `2.0.0`
- Project URL: https://github.com/NetTopologySuite/NetTopologySuite.Features
- License: `BSD-3-Clause`
- SPDX / license URL: https://licenses.nuget.org/BSD-3-Clause

RhinoSpatial uses NetTopologySuite.Features as part of local Shapefile feature parsing.

### NetTopologySuite Esri Shapefile IO

- Package: `NetTopologySuite.IO.Esri.Shapefile`
- Version: `1.2.0`
- Project URL: https://github.com/NetTopologySuite/NetTopologySuite.IO.Esri
- License: `BSD-3-Clause`
- SPDX / license URL: https://licenses.nuget.org/BSD-3-Clause

RhinoSpatial uses NetTopologySuite.IO.Esri.Shapefile to read local `.shp` vector sources.

### ProjNET

- Package: `ProjNET`
- Version: `2.1.0`
- Project URL: https://github.com/NetTopologySuite/ProjNet4GeoAPI
- License: `LGPL-2.1-or-later`
- SPDX / license URL: https://licenses.nuget.org/LGPL-2.1-or-later

RhinoSpatial uses ProjNET for spatial reference and coordinate transformation support.

### GeographicLib EGM96 Geoid Grid

- Data file: `egm96-15.pgm`
- Source: GeographicLib geoid data distribution
- Model: WGS84 EGM96, 15-minute global geoid grid
- Source URL: https://sourceforge.net/projects/geographiclib/files/geoids-distrib/egm96-15.zip/
- Documentation: https://geographiclib.sourceforge.io/html/GeoidEval.1.html

RhinoSpatial embeds this grid to convert WGS84 ellipsoid heights to approximate sea-level/geoid heights for globally available vertical alignment.

### Mapzen Terrain Tiles

- Dataset/service: Mapzen Terrain Tiles / AWS `elevation-tiles-prod`
- Format used: Skadi HGT tiles in unprojected WGS84 coordinates
- Source URL: https://s3.amazonaws.com/elevation-tiles-prod/skadi/
- Documentation: https://www.mapzen.com/blog/terrain-tile-service/
- Attribution guidance: https://www.mapzen.com/rights/

RhinoSpatial uses Mapzen Skadi terrain tiles as the built-in global terrain fallback when no user terrain service URL is provided.

## Notes

- RhinoSpatial itself remains licensed under MIT.
- These third-party libraries are distributed as separate assemblies in the release package.
- Original copyrights, notices, and licenses for those libraries continue to apply to those libraries.
