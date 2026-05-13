# Test Sources

This file collects public sources that are useful for RhinoSpatial compatibility testing.

The goal is not to recommend these providers for production work. The goal is to keep a broad, practical regression set that covers different countries, server stacks, coordinate systems, formats, and source types.

Use these together with:

- `examples/sources.json`
- `examples/VALIDATION.md`
- the sandbox probe command:
  `dotnet run --project RhinoSpatial.Sandbox.csproj -- probe <wfs|wms|wcs> <url>`

## Current Tested Sources

### WFS / Vector

| ID | Region | Source | URL | Current Use |
| --- | --- | --- | --- | --- |
| `wfs-france-geopf-adminexpress` | France / overseas territories | IGN Geoplateforme WFS | `https://data.geopf.fr/wfs/ows?SERVICE=WFS&VERSION=2.0.0&REQUEST=GetCapabilities` | Capabilities and `ADMINEXPRESS-COG-CARTO-PE.2025:commune` GetFeature parsed successfully. Good broad non-German polygon test. |
| `wfs-netherlands-pdok-boundaries` | Netherlands | PDOK administrative boundaries WFS | `https://service.pdok.nl/kadaster/bestuurlijkegebieden/wfs/v1_0?service=WFS&version=2.0.0&request=GetCapabilities` | Capabilities and `bestuurlijkegebieden:Gemeentegebied` GetFeature parsed successfully. Good EPSG:28992 / EPSG:4326 provider test. |
| `wfs-mapserver-demo-world` | Global demo | MapServer demo WFS | `https://demo.mapserver.org/cgi-bin/wfs?SERVICE=WFS&VERSION=2.0.0&REQUEST=GetCapabilities` | Capabilities and `ms:cities` GetFeature parsed successfully. Useful lightweight point/polygon parser smoke test. |
| `wfs-germany-vg1000` | Germany | BKG administrative boundaries WFS | `https://sgx.geodatenzentrum.de/wfs_vg1000?service=WFS&request=GetCapabilities` | Existing Germany-wide official WFS baseline. |
| `wfs-hessen-reference` | Germany / Hessen | Hessen WFS | `https://inspire-hessen.de/ows/services/org.2.734621f7-1cfb-41c6-8291-67d08f149acb_wfs?` | Existing multi-layer WFS baseline. |

### WMS / Imagery And Map Context

| ID | Region | Source | URL | Current Use |
| --- | --- | --- | --- | --- |
| `wms-nasa-gibs` | Global | NASA GIBS WMS | `https://gibs.earthdata.nasa.gov/wms/epsg4326/best/wms.cgi?SERVICE=WMS&REQUEST=GetCapabilities` | Capabilities parsed successfully. Good large global WMS and EPSG:4326 test. |
| `wms-switzerland-geoadmin` | Switzerland | swisstopo / geo.admin.ch WMS | `https://wms.geo.admin.ch/?SERVICE=WMS&REQUEST=GetCapabilities` | Capabilities parsed successfully. Good EPSG:2056 / EPSG:4326 / EPSG:3857 test. |
| `wms-netherlands-pdok-aerial` | Netherlands | PDOK aerial imagery WMS | `https://service.pdok.nl/hwh/luchtfotorgb/wms/v1_0?service=WMS&request=GetCapabilities` | Capabilities parsed successfully. Good aerial-imagery and EPSG:28992 test. |
| `wms-usgs-topo` | United States | USGS National Map Topo WMS | `https://basemap.nationalmap.gov/arcgis/services/USGSTopo/MapServer/WMSServer?request=GetCapabilities&service=WMS` | Capabilities parsed successfully. Good ArcGIS WMS and global-ish web-map test. |
| `wms-terrestris-osm` | Global | terrestris OSM WMS | `https://ows.terrestris.de/osm/service?SERVICE=WMS&REQUEST=GetCapabilities` | Capabilities parsed successfully. Good generic fallback/context WMS test. |
| `wms-germany-topplusopen` | Germany | BKG TopPlusOpen WMS | `https://sgx.geodatenzentrum.de/wms_topplus_open?request=GetCapabilities&service=wms` | Existing Germany-wide official map context baseline. |

### Terrain / WCS

| ID | Region | Source | URL | Current Use |
| --- | --- | --- | --- | --- |
| `terrain-global-skadi-fallback` | Global land areas | Built-in Skadi fallback | none | Built-in terrain fallback. Must stay fast and fail clearly for oversized contexts. |
| `wcs-germany-dgm200` | Germany | BKG DGM200 WCS | `https://sgx.geodatenzentrum.de/wcs_dgm200_inspire?SERVICE=WCS&REQUEST=GetCapabilities` | Capabilities parsed successfully. Official coarse Germany terrain baseline. |
| `terrain-nrw-dgm` | Germany / NRW | NRW DGM WCS | `https://www.wcs.nrw.de/geobasis/wcs_nw_dgm?REQUEST=GetCapabilities&SERVICE=WCS` | Capabilities parsed successfully. Good state-level WCS behavior check. |
| `wcs-rasdaman-demo` | Global / sample coverages | rasdaman OGC WCS demo | `https://ows.rasdaman.org/rasdaman/ows?SERVICE=WCS&REQUEST=GetCapabilities` | Capabilities parsed successfully. Good non-German WCS 2.1 provider/parser stress test; not a project-data recommendation. |

### LoD2 / CityGML Buildings

| ID | Region | Source | URL | Current Use |
| --- | --- | --- | --- | --- |
| `lod2-hessen-buildings` | Germany / Hessen | Hessen LoD2 WFS | `https://www.geoportal.hessen.de/mapbender/php/wfs.php?FEATURETYPE_ID=5589&INSPIRE=1&REQUEST=GetCapabilities&SERVICE=WFS&VERSION=2.0.0` | Existing LoD2 WFS baseline. Good for terrain/building alignment. |
| `lod2-sachsen-anhalt` | Germany / Sachsen-Anhalt | Sachsen-Anhalt LoD2 WFS | `https://www.geodatenportal.sachsen-anhalt.de/wss/service/ST_LVermGeo_LoD2_WFS/guest?request=GetCapabilities&service=WFS&version=2.0.0` | Existing second-state LoD2 WFS baseline. |
| `citygml-ogc-building-lod2` | Format sample | OGC CityGML 2.0 LoD2 building sample | `https://schemas.opengis.net/citygml/examples/2.0/building/Building_LOD2-EPSG25832.gml` | Downloaded and parsed successfully with `lod2file`; 1 building / 7 surfaces. |
| `citygml-ogc-building-garage-lod2` | Format sample | OGC CityGML 2.0 LoD2 building + garage sample | `https://schemas.opengis.net/citygml/examples/2.0/building/Building_and_garage_LOD2-EPSG25832.gml` | Downloaded and parsed successfully with `lod2file`; 1 building / 13 surfaces. |

### GeoTIFF / Local Raster

| ID | Region | Source | URL | Current Use |
| --- | --- | --- | --- | --- |
| `geotiff-osgeo-byte` | Sample | OSGeo GDAL small projected GeoTIFF | `https://raw.githubusercontent.com/OSGeo/gdal/master/autotest/gcore/data/byte.tif` | Downloaded and read successfully; EPSG:26711, 20 x 20. |
| `geotiff-osgeo-rgbsmall` | Sample | OSGeo GDAL small WGS84 RGB GeoTIFF | `https://raw.githubusercontent.com/OSGeo/gdal/master/autotest/gcore/data/rgbsmall.tif` | Downloaded and read successfully; EPSG:4326, 50 x 50. |
| `geotiff-local` | User/project data | Bring-your-own GeoTIFF | none | Main GeoTIFF workflow remains local file input. |

### OSM

| ID | Region | Source | URL | Current Use |
| --- | --- | --- | --- | --- |
| `osm-default` | Global where Overpass has coverage | Built-in Overpass workflow | none | Curated Buildings / Road / Water / Green / Rail contextual outputs. |

### Google 3D Tiles Viewer

| ID | Region | Source | URL | Current Use |
| --- | --- | --- | --- | --- |
| `google-3d-tiles-viewer` | Coverage depends on Google | Google Photorealistic 3D Tiles | none in repo | Requires user-managed API key. Reference viewer only; do not commit keys or cached/exported Google content. |

## Rejected Or Watch-List Sources

| Source | Result | Note |
| --- | --- | --- |
| US Census TIGERweb WFS ArcGIS endpoint | `400 Bad Request` during WFS GetCapabilities probe | Useful reminder that some ArcGIS endpoints expose WMS/REST cleanly but not always WFS in a way RhinoSpatial can consume. Keep out of examples until a stable WFS URL is confirmed. |
| `https://wfs.geo.admin.ch/` | DNS failure during probe | swisstopo WMS works well; no WFS baseline from this hostname was confirmed. |

## Format Gaps Worth Considering

These are not release blockers, but they are the most sensible future source-format candidates:

- **Local DEM / terrain files**
  GeoTIFF DEM should be the first file-based terrain input to consider, because the project already has GeoTIFF reading and terrain mesh output. Esri ASCII Grid and XYZ/CSV elevation grids are possible later if real users need them.
- **GeoPackage**
  Good candidate for local vector/project data because it can hold layers, CRS metadata, and mixed geometry more robustly than loose shapefiles.
- **Shapefile**
  Still useful for municipalities, but should probably be a vector/building-file source path rather than being hidden inside LoD2. Shapefiles may contain 2D footprints, attributes, or occasional 3D geometry depending on provider.
- **CityJSON**
  Worth watching for local 3D city/building models. It may be easier to parse than CityGML, but it should only be added after the CityGML/LoD2 path is stable.
- **Cloud Optimized GeoTIFF**
  Could improve remote raster workflows later, but the current `Load GeoTIFF` component is intentionally local-file oriented.
