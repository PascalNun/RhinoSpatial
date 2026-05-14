# Test Sources

This file collects public sources that are useful for RhinoSpatial compatibility testing.

The goal is not to recommend these providers for production work. The goal is to keep a broad, practical regression set that covers different countries, server stacks, coordinate systems, formats, and source types.

Use these together with:

- `examples/sources.json`
- `examples/VALIDATION.md`
- the sandbox probe command:
  `dotnet run --project RhinoSpatial.Sandbox.csproj -- probe <wfs|wms|wcs> <url>`
- local-file smoke commands:
  `dotnet run --project RhinoSpatial.Sandbox.csproj -- lod2file <path>`,
  `dotnet run --project RhinoSpatial.Sandbox.csproj -- geotiff <path>`,
  `dotnet run --project RhinoSpatial.Sandbox.csproj -- terrainfile <path>`,
  and `dotnet run --project RhinoSpatial.Sandbox.csproj -- shapefile <path>`

## Current Tested Sources

### WFS / Vector

| ID | Region | Source | URL | Current Use |
| --- | --- | --- | --- | --- |
| `wfs-france-geopf-adminexpress` | France / overseas territories | IGN Geoplateforme WFS | `https://data.geopf.fr/wfs/ows?SERVICE=WFS&VERSION=2.0.0&REQUEST=GetCapabilities` | Capabilities and `ADMINEXPRESS-COG-CARTO-PE.2025:commune` GetFeature parsed successfully. Good broad non-German polygon test. |
| `wfs-netherlands-pdok-boundaries` | Netherlands | PDOK administrative boundaries WFS | `https://service.pdok.nl/kadaster/bestuurlijkegebieden/wfs/v1_0?service=WFS&version=2.0.0&request=GetCapabilities` | Capabilities and `bestuurlijkegebieden:Gemeentegebied` GetFeature parsed successfully. Good EPSG:28992 / EPSG:4326 provider test. |
| `wfs-netherlands-pdok-bag` | Netherlands | PDOK BAG WFS | `https://service.pdok.nl/kadaster/bag/wfs/v2_0?request=getCapabilities&service=WFS` | Capabilities parsed successfully; exposes BAG layers such as `bag:pand`. Useful EPSG:28992 official-building/provider compatibility source. |
| `wfs-belgium-vlaanderen-grb` | Belgium / Flanders | Digitaal Vlaanderen GRB WFS | `https://geo.api.vlaanderen.be/GRB/wfs?SERVICE=WFS&REQUEST=GetCapabilities` | Capabilities parsed successfully; 27 layers, default EPSG:31370. Good watch source for providers outside the currently common Spatial Context SRS set. |
| `wfs-mapserver-demo-world` | Global demo | MapServer demo WFS | `https://demo.mapserver.org/cgi-bin/wfs?SERVICE=WFS&VERSION=2.0.0&REQUEST=GetCapabilities` | Capabilities and `ms:cities` GetFeature parsed successfully. Useful lightweight point/polygon parser smoke test. |
| `wfs-geoserver-demo-naturalearth` | Global demo | GeoServer Natural Earth demo WFS | `https://ahocevar.com/geoserver/wfs?SERVICE=WFS&REQUEST=GetCapabilities` | Capabilities parsed successfully. Good GeoServer demo service with Natural Earth layers and EPSG:4326 defaults. |
| `shapefile-naturalearth-countries` | Global sample | Natural Earth admin countries Shapefile | `https://naciscdn.org/naturalearth/110m/cultural/ne_110m_admin_0_countries.zip` | Downloaded and parsed successfully with the sandbox `shapefile` command; 177 features, EPSG:4326. Good local Shapefile/vector smoke test for `Load WFS` local file mode. |
| `wfs-germany-vg1000` | Germany | BKG administrative boundaries WFS | `https://sgx.geodatenzentrum.de/wfs_vg1000?service=WFS&request=GetCapabilities` | Existing Germany-wide official WFS baseline. |
| `wfs-hessen-reference` | Germany / Hessen | Hessen WFS | `https://inspire-hessen.de/ows/services/org.2.734621f7-1cfb-41c6-8291-67d08f149acb_wfs?` | Existing multi-layer WFS baseline. |
| `wfs-hessen-building-footprints` | Germany / Hessen | Hessen building footprints WFS | `https://www.geoportal.hessen.de/mapbender/php/wfs.php?FEATURETYPE_ID=5042&INSPIRE=1&REQUEST=GetCapabilities&SERVICE=WFS&VERSION=2.0.0` | Existing official 2D building footprint baseline for comparison with LoD2 and OSM. |
| `wfs-hessen-parcels` | Germany / Hessen | Hessen cadastral parcels WFS | `https://www.geoportal.hessen.de/registry/wfs/710?REQUEST=GetCapabilities&SERVICE=WFS&VERSION=2.0.0` | Existing dense polygon / cadastral context baseline. |
| `wfs-hessen-roads` | Germany / Hessen | Hessen roads WFS | `https://www.geoportal.hessen.de/registry/wfs/723?REQUEST=GetCapabilities&SERVICE=WFS&VERSION=2.0.0` | Existing line-feature baseline for comparing official transport data with OSM. |
| `wfs-hessen-addresses` | Germany / Hessen | Hessen addresses WFS | `https://www.geoportal.hessen.de/registry/wfs/706?REQUEST=GetCapabilities&SERVICE=WFS&VERSION=2.0.0` | Existing point-feature baseline. |
| `wfs-sachsen-alkis-parcels` | Germany / Sachsen | Sachsen ALKIS simplified parcels WFS | `https://geodienste.sachsen.de/aaa/public_alkis/vereinf/wfs?SERVICE=WFS&VERSION=2.0.0&REQUEST=GetCapabilities` | Regression source for provider/version/SRS handling after a previous `400 Bad Request` on direct parcel requests. |

### WMS / Imagery And Map Context

| ID | Region | Source | URL | Current Use |
| --- | --- | --- | --- | --- |
| `wms-nasa-gibs` | Global | NASA GIBS WMS | `https://gibs.earthdata.nasa.gov/wms/epsg4326/best/wms.cgi?SERVICE=WMS&REQUEST=GetCapabilities` | Capabilities parsed successfully. Good large global WMS and EPSG:4326 test. |
| `wms-switzerland-geoadmin` | Switzerland | swisstopo / geo.admin.ch WMS | `https://wms.geo.admin.ch/?SERVICE=WMS&REQUEST=GetCapabilities` | Capabilities parsed successfully. Good EPSG:2056 / EPSG:4326 / EPSG:3857 test. |
| `wms-netherlands-pdok-aerial` | Netherlands | PDOK aerial imagery WMS | `https://service.pdok.nl/hwh/luchtfotorgb/wms/v1_0?service=WMS&request=GetCapabilities` | Capabilities parsed successfully. Good aerial-imagery and EPSG:28992 test. |
| `wms-usgs-topo` | United States | USGS National Map Topo WMS | `https://basemap.nationalmap.gov/arcgis/services/USGSTopo/MapServer/WMSServer?request=GetCapabilities&service=WMS` | Capabilities parsed successfully. Good ArcGIS WMS and global-ish web-map test. |
| `wms-terrestris-osm` | Global | terrestris OSM WMS | `https://ows.terrestris.de/osm/service?SERVICE=WMS&REQUEST=GetCapabilities` | Capabilities parsed successfully; `OSM-WMS` supports EPSG:3857 and advertises 8000 x 8000 max images. Current first built-in WMS fallback. |
| `wms-terrestris-osm-gray` | Global | terrestris OSM gray WMS | `https://ows.terrestris.de/osm-gray/service?SERVICE=WMS&REQUEST=GetCapabilities` | Capabilities parsed successfully. Useful alternate OSM-style WMS context/fallback candidate. |
| `wms-mundialis-osm` | Global | mundialis OSM WMS | `https://ows.mundialis.de/services/service?SERVICE=WMS&REQUEST=GetCapabilities` | Capabilities parsed successfully. Useful alternate global OSM-style WMS reference. |
| `wms-belgium-vlaanderen-grb` | Belgium / Flanders | Digitaal Vlaanderen GRB WMS | `https://geo.api.vlaanderen.be/GRB/wms?SERVICE=WMS&REQUEST=GetCapabilities` | Capabilities parsed successfully; 43 layers and support for EPSG:4326 / EPSG:4258 / EPSG:31370 / EPSG:3812. Good alternate-CRS request test. |
| `wms-estonia-maaamet-base` | Estonia | Maa-amet base WMS | `https://kaart.maaamet.ee/wms/alus?SERVICE=WMS&REQUEST=GetCapabilities` | Capabilities parsed successfully; EPSG:3301-only source. Keep as a known limitation/watch source until broader CRS support exists. |
| `wms-germany-topplusopen` | Germany | BKG TopPlusOpen WMS | `https://sgx.geodatenzentrum.de/wms_topplus_open?request=GetCapabilities&service=wms` | Existing Germany-wide official map context baseline. |
| `wms-hessen-imagery` | Germany / Hessen | Hessen orthophoto imagery WMS | `https://www.gds-srv.hessen.de/cgi-bin/lika-services/ogc-free-images.ows?language=ger&SERVICE=WMS&VERSION=1.3.0&REQUEST=GetCapabilities` | Existing high-resolution Hessen imagery baseline. |
| `wms-hessen-cadastral` | Germany / Hessen | Hessen cadastral map WMS | `https://www.geoportal.hessen.de/mapbender/php/wms.php?REQUEST=GetCapabilities&SERVICE=WMS&inspire=1&layer_id=54097&withChilds=1` | Existing cadastral/linework WMS baseline. |
| `wms-spain-pnoa` | Spain | IGN PNOA orthophoto WMS | `https://www.ign.es/wms-inspire/pnoa-ma?SERVICE=WMS&REQUEST=GetCapabilities` | Capabilities parsed successfully; good non-German orthophoto source with `OI.OrthoimageCoverage`. |
| `wms-france-geopf-raster` | France | IGN Geoplateforme raster WMS | `https://data.geopf.fr/wms-r/wms?SERVICE=WMS&REQUEST=GetCapabilities` | Capabilities parsed successfully; large layer catalogue and 5010 x 5010 max image limit. |
| `wms-geoserver-demo-naturalearth` | Global demo | GeoServer Natural Earth demo WMS | `https://ahocevar.com/geoserver/wms?SERVICE=WMS&REQUEST=GetCapabilities` | Capabilities parsed successfully. Good lightweight GeoServer WMS regression source. |
| `wms-norway-geonorge-topo` | Norway | Geonorge / Kartverket topographic WMS | `https://wms.geonorge.no/skwms1/wms.topo?service=WMS&request=GetCapabilities` | Capabilities parsed successfully; 226 layers and 8192 x 8192 max images. Good Nordic/national WMS provider test. |

### Terrain / WCS

| ID | Region | Source | URL | Current Use |
| --- | --- | --- | --- | --- |
| `terrain-global-skadi-fallback` | Global land areas | Built-in Skadi fallback | none | Built-in terrain fallback. Must stay fast and fail clearly for oversized contexts. |
| `wcs-germany-dgm200` | Germany | BKG DGM200 WCS | `https://sgx.geodatenzentrum.de/wcs_dgm200_inspire?SERVICE=WCS&REQUEST=GetCapabilities` | Capabilities parsed successfully. Official coarse Germany terrain baseline. |
| `terrain-hessen-dgm1` | Germany / Hessen | Hessen DGM1 WCS | `https://inspire-hessen.de/raster/dgm1/ows?REQUEST=GetCapabilities&SERVICE=WCS&VERSION=2.1.0` | Existing high-resolution Hessen terrain baseline. |
| `terrain-nrw-dgm` | Germany / NRW | NRW DGM WCS | `https://www.wcs.nrw.de/geobasis/wcs_nw_dgm?REQUEST=GetCapabilities&SERVICE=WCS` | Capabilities parsed successfully. Good state-level WCS behavior check. |
| `terrain-netherlands-ahn` | Netherlands | PDOK AHN WCS | `https://service.pdok.nl/rws/ahn/wcs/v1_0?service=WCS&request=GetCapabilities` | Capabilities parsed successfully; exposes `dsm_05m` and `dtm_05m`. Good non-German terrain/WCS test. |
| `wcs-rasdaman-demo` | Global / sample coverages | rasdaman OGC WCS demo | `https://ows.rasdaman.org/rasdaman/ows?SERVICE=WCS&REQUEST=GetCapabilities` | Capabilities parsed successfully. Good non-German WCS 2.1 provider/parser stress test; not a project-data recommendation. |

### LoD2 / CityGML Buildings

| ID | Region | Source | URL | Current Use |
| --- | --- | --- | --- | --- |
| `lod2-hessen-buildings` | Germany / Hessen | Hessen LoD2 WFS | `https://www.geoportal.hessen.de/mapbender/php/wfs.php?FEATURETYPE_ID=5589&INSPIRE=1&REQUEST=GetCapabilities&SERVICE=WFS&VERSION=2.0.0` | Existing LoD2 WFS baseline. Good for terrain/building alignment. |
| `lod2-sachsen-anhalt` | Germany / Sachsen-Anhalt | Sachsen-Anhalt LoD2 WFS | `https://www.geodatenportal.sachsen-anhalt.de/wss/service/ST_LVermGeo_LoD2_WFS/guest?request=GetCapabilities&service=WFS&version=2.0.0` | Existing second-state LoD2 WFS baseline. |
| `citygml-ogc-building-lod2` | Format sample | OGC CityGML 2.0 LoD2 building sample | `https://schemas.opengis.net/citygml/examples/2.0/building/Building_LOD2-EPSG25832.gml` | Downloaded and parsed successfully with `lod2file`; 1 building / 7 surfaces. |
| `citygml-ogc-building-garage-lod2` | Format sample | OGC CityGML 2.0 LoD2 building + garage sample | `https://schemas.opengis.net/citygml/examples/2.0/building/Building_and_garage_LOD2-EPSG25832.gml` | Downloaded and parsed successfully with `lod2file`; 1 building / 13 surfaces. |
| `cityjson-local` | User/project data | Bring-your-own CityJSON building file | none | `Load LoD2 Buildings` now accepts local `.json` / `.cityjson` files through the same `LoD2 Source` input. Sandbox smoke test parsed a simple CityJSON solid as 1 building / 6 surfaces. |

### GeoTIFF / Local Raster

| ID | Region | Source | URL | Current Use |
| --- | --- | --- | --- | --- |
| `geotiff-osgeo-byte` | Sample | OSGeo GDAL small projected GeoTIFF | `https://raw.githubusercontent.com/OSGeo/gdal/master/autotest/gcore/data/byte.tif` | Downloaded and read successfully; EPSG:26711, 20 x 20. |
| `geotiff-osgeo-rgbsmall` | Sample | OSGeo GDAL small WGS84 RGB GeoTIFF | `https://raw.githubusercontent.com/OSGeo/gdal/master/autotest/gcore/data/rgbsmall.tif` | Downloaded and read successfully; EPSG:4326, 50 x 50. |
| `geotiff-local` | User/project data | Bring-your-own GeoTIFF | none | Main GeoTIFF workflow remains local file input. |
| `terrainfile-local-geotiff-dem` | User/project data | Bring-your-own local GeoTIFF DEM | none | `Load Terrain` now accepts local `.tif/.tiff` DEM paths as a terrain source. Tested with the sandbox `terrainfile` command on `geotiff-osgeo-byte`. |

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
| Czech CUZK orthophoto ArcGIS WMS candidate | `400 Bad Request` during WMS GetCapabilities probe | Keep as a provider watch item; ArcGIS WMS URL normalization may need source-specific handling before it is useful as a regression source. |
| Geoscience Australia topographic ArcGIS WMS candidate | `404 Not Found` during WMS GetCapabilities probe | Keep out of examples until a stable public endpoint is confirmed. |
| EOX WCS endpoint | Timed out during WCS capabilities probe | `https://ows.eox.at/eoxserver/ows?SERVICE=WCS&REQUEST=GetCapabilities` is useful as a timeout/provider-resilience watch item, but not a current regression source. |
| pygeoapi lakes endpoint | Failed as WFS because it is OGC API Features / JSON, not WFS | `https://demo.pygeoapi.io/master/collections/lakes/items?f=json` is a good future OGC API Features gap, not a WFS source. |

## Format Gaps Worth Considering

These are not release blockers, but they are the most sensible future source-format candidates:

- **Local DEM / terrain files**
  GeoTIFF DEM is now the first file-based terrain input: `Load Terrain` accepts local `.tif/.tiff` DEM paths. Esri ASCII Grid and XYZ/CSV elevation grids remain possible later if real users need them.
- **CityJSON**
  Basic local CityJSON building import is now supported through `Load LoD2 Buildings`. More real CityJSON sources should be added to this test catalogue before calling it mature.
- **GeoPackage**
  Good candidate for local vector/project data because it can hold layers, CRS metadata, and mixed geometry more robustly than loose shapefiles. It still needs a deliberate dependency/parser choice.
- **Shapefile**
  Local Shapefile is now supported as a vector source through `Load WFS`, using the existing shared Spatial Context and geometry tree output. It is not treated as LoD2 building import; many municipal Shapefiles are 2D footprints or thematic vector layers rather than true LoD2 geometry.
- **OGC API Features**
  Modern JSON feature endpoints are increasingly common and can look attractive as simple URLs, but they are not WFS. A dedicated source path would be cleaner than pretending they are WFS.
- **Cloud Optimized GeoTIFF**
  Could improve remote raster workflows later, but the current `Load GeoTIFF` component is intentionally local-file oriented.
