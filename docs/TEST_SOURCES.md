# Test Sources

This file collects public sources that are useful for RhinoSpatial compatibility testing.

The goal is not to recommend these providers for production work. The goal is to keep a broad, practical regression set that covers different countries, server stacks, coordinate systems, formats, and source types.

The **Current Use** column describes what the source is supposed to validate in RhinoSpatial: parser coverage, provider quirks, CRS behavior, terrain/raster alignment, local-file handling, or manual visual context. If a source is on the watch list, it is intentionally not a recommended example yet.

Use these together with:

- `examples/sources.json`
- `examples/VALIDATION.md`
- the sandbox probe command:
  `dotnet run --project RhinoSpatial.Sandbox.csproj -- probe <wfs|wms|wcs> <url>`
- bounded service request probes:
  `dotnet run --project RhinoSpatial.Sandbox.csproj -- wfsfeature <capabilities-url> <layer> <srs> <minX,minY,maxX,maxY> [max-features]`,
  `dotnet run --project RhinoSpatial.Sandbox.csproj -- wmsimage <capabilities-url> <layer> <srs> <minX,minY,maxX,maxY> [width] [height]`,
  and `dotnet run --project RhinoSpatial.Sandbox.csproj -- wcscoverage <capabilities-url> <coverage> <minX,minY,maxX,maxY> [srs]`
- local-file smoke commands:
  `dotnet run --project RhinoSpatial.Sandbox.csproj -- lod2file <path>`,
  `dotnet run --project RhinoSpatial.Sandbox.csproj -- geotiff <path>`,
  `dotnet run --project RhinoSpatial.Sandbox.csproj -- terrainfile <path>`,
  `dotnet run --project RhinoSpatial.Sandbox.csproj -- shapefile <path>`,
  `dotnet run --project RhinoSpatial.Sandbox.csproj -- geojson <path>`,
  and `dotnet run --project RhinoSpatial.Sandbox.csproj -- ogcapi <items-url>`
- Google 3D Tiles refinement, geographic-bounds, and shared-elevation-baseline check:
  `dotnet run --project RhinoSpatial.Sandbox.csproj -- tiles-selection-check`
- Spatial Context local-reference metadata check:
  `dotnet run --project RhinoSpatial.Sandbox.csproj -- reference-source-check`
- WFS/WMS request-axis check:
  `dotnet run --project RhinoSpatial.Sandbox.csproj -- ogc-request-check`

## Current Tested Sources

### WFS / Vector

| ID | Region | Source | URL | Current Use |
| --- | --- | --- | --- | --- |
| `wfs-france-geopf-adminexpress` | France / overseas territories | IGN Geoplateforme WFS | `https://data.geopf.fr/wfs/ows?SERVICE=WFS&VERSION=2.0.0&REQUEST=GetCapabilities` | Capabilities and `ADMINEXPRESS-COG-CARTO-PE.2025:commune` GetFeature parsed successfully. Good broad non-German polygon test. |
| `wfs-netherlands-pdok-boundaries` | Netherlands | PDOK administrative boundaries WFS | `https://service.pdok.nl/kadaster/bestuurlijkegebieden/wfs/v1_0?service=WFS&version=2.0.0&request=GetCapabilities` | Capabilities and `bestuurlijkegebieden:Gemeentegebied` GetFeature parsed successfully. Good EPSG:28992 / EPSG:4326 provider test. |
| `wfs-netherlands-pdok-bag` | Netherlands | PDOK BAG WFS | `https://service.pdok.nl/kadaster/bag/wfs/v2_0?request=getCapabilities&service=WFS` | Capabilities parsed successfully; exposes BAG layers such as `bag:pand`. Useful EPSG:28992 official-building/provider compatibility source. |
| `wfs-belgium-vlaanderen-grb` | Belgium / Flanders | Digitaal Vlaanderen GRB WFS | `https://geo.api.vlaanderen.be/GRB/wfs?SERVICE=WFS&REQUEST=GetCapabilities` | Capabilities parsed successfully; 27 layers, default EPSG:31370. Good watch source for providers outside the currently common Spatial Context SRS set. |
| `wfs-canada-geomet` | Canada | Environment and Climate Change Canada GeoMet WFS | `https://geo.weather.gc.ca/geomet?SERVICE=WFS&REQUEST=GetCapabilities` | Capabilities parsed successfully; 97 layers, EPSG:4326 defaults. Good large public WFS capabilities and weather-feature provider test. |
| `wfs-newzealand-gns-geology` | New Zealand | GNS Science geology WFS | `https://maps.gns.cri.nz/geology/wfs?SERVICE=WFS&REQUEST=GetCapabilities` | Capabilities parsed successfully; 55 layers, default EPSG:2193. Good southern-hemisphere WFS and unsupported-native-CRS watch source. |
| `wfs-spain-catastro-parcels` | Spain | Spanish Cadastre INSPIRE parcels WFS | `https://ovc.catastro.meh.es/INSPIRE/wfsCP.aspx?service=WFS&request=GetCapabilities` | Capabilities parsed successfully; 2 parcel/zoning layers, default EPSG:4326 URN. Intended to validate cadastral WFS behavior, INSPIRE naming, and dense parcel-style polygons outside Germany. |
| `wfs-czech-cuzk-parcels` | Czechia | CUZK INSPIRE cadastral parcels WFS | `https://services.cuzk.cz/wfs/inspire-cp-wfs.asp?service=WFS&request=GetCapabilities` | Capabilities parsed successfully; 3 parcel/boundary/zoning layers, native EPSG:5514. Intended as a cadastral WFS and unsupported-native-CRS watch source. |
| `wfs-uk-bgs-geology-625k` | United Kingdom | British Geological Survey 625k geology WFS | `https://ogc.bgs.ac.uk/digmap625k_gsml_insp_gs/wfs?service=WFS&request=GetCapabilities` | Capabilities parsed successfully; 11 GeoSciML/INSPIRE layers, default EPSG:27700. Intended to validate complex feature/layer metadata and UK CRS handling. |
| `wfs-austria-vienna-open-data` | Austria / Vienna | Vienna Open Government Data WFS | `https://data.wien.gv.at/daten/geo?service=WFS&request=GetCapabilities&version=1.1.0` | Bounded `ogdwien:ADRESSENOGD` request parsed 25 point features in EPSG:4326. Also validates an HTTPS capabilities entry point whose operation URL is advertised as HTTP. |
| `wfs-finland-nls-administrative-units` | Finland | National Land Survey INSPIRE administrative units WFS | `https://inspire-wfs.maanmittauslaitos.fi/inspire-wfs/au/ows?request=GetCapabilities&service=wfs&version=2.0.0` | Bounded `au:AdministrativeUnit` request around Helsinki parsed 3 polygons in EPSG:4326. Good WFS 2.0 / native EPSG:3067 provider test. |
| `wfs-mapserver-demo-world` | Global demo | MapServer demo WFS | `https://demo.mapserver.org/cgi-bin/wfs?SERVICE=WFS&VERSION=2.0.0&REQUEST=GetCapabilities` | Capabilities and `ms:cities` GetFeature parsed successfully. Useful lightweight point/polygon parser smoke test. |
| `wfs-geoserver-demo-naturalearth` | Global demo | GeoServer Natural Earth demo WFS | `https://ahocevar.com/geoserver/wfs?SERVICE=WFS&REQUEST=GetCapabilities` | Capabilities parsed successfully. Good GeoServer demo service with Natural Earth layers and EPSG:4326 defaults. |
| `shapefile-naturalearth-countries` | Global sample | Natural Earth admin countries Shapefile | `https://naciscdn.org/naturalearth/110m/cultural/ne_110m_admin_0_countries.zip` | Downloaded and parsed successfully with the sandbox `shapefile` command; 177 features, EPSG:4326. Good local Shapefile/vector smoke test for `Load WFS` local file mode. |
| `shapefile-naturalearth-populated-places` | Global sample | Natural Earth populated places Shapefile | `https://naciscdn.org/naturalearth/110m/cultural/ne_110m_populated_places.zip` | Downloaded and parsed successfully with the sandbox `shapefile` command; 243 point features, EPSG:4326. Good local Shapefile point-feature smoke test. |
| `shapefile-naturalearth-roads` | Global sample | Natural Earth roads Shapefile | `https://naciscdn.org/naturalearth/10m/cultural/ne_10m_roads.zip` | Downloaded and parsed successfully with the sandbox `shapefile` command; 56,600 line features, EPSG:4326. Good larger local Shapefile line-feature/performance smoke test. |
| `shapefile-naturalearth-rivers` | Global sample | Natural Earth rivers and lake centerlines Shapefile | `https://naciscdn.org/naturalearth/10m/physical/ne_10m_rivers_lake_centerlines.zip` | Downloaded and parsed successfully with the sandbox `shapefile` command; 1,473 line features, EPSG:4326. Intended to validate physical linework and local Shapefile line output. |
| `shapefile-naturalearth-urban-areas` | Global sample | Natural Earth urban areas Shapefile | `https://naciscdn.org/naturalearth/10m/cultural/ne_10m_urban_areas.zip` | Downloaded and parsed successfully with the sandbox `shapefile` command; 11,878 polygon features, EPSG:4326. Intended to validate larger local polygon Shapefiles and performance. |
| `geojson-local` | User/project data | Bring-your-own GeoJSON vector file | none | `Load WFS` now accepts local `.geojson` / GeoJSON `.json` files through the same vector input. GeoJSON defaults to EPSG:4326 unless an older named `crs` member is present. |
| `ogcapi-pygeoapi-lakes` | Global demo | pygeoapi OGC API Features lakes collection | `https://demo.pygeoapi.io/master/collections/lakes/items?f=json` | Parsed successfully with the sandbox `ogcapi` command. Intended to validate modern OGC API Features GeoJSON endpoints through the vector loader without treating them as WFS. |
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
| `wms-canada-geomet` | Canada | Environment and Climate Change Canada GeoMet WMS | `https://geo.weather.gc.ca/geomet?SERVICE=WMS&REQUEST=GetCapabilities` | Capabilities parsed successfully; very large catalogue with 8,893 layers and 16,384 x 16,384 advertised max images. Good stress test for large capabilities documents. |
| `wms-noaa-nowcoast-radar` | United States / coastal regions | NOAA nowCOAST radar WMS | `https://nowcoast.noaa.gov/geoserver/observations/weather_radar/ows?service=wms&version=1.3.0&request=GetCapabilities` | Capabilities parsed successfully; 6 weather-radar layers with EPSG:3857, EPSG:4326, and CRS:84. Good GeoServer WMS and CRS alias test. |
| `wms-newzealand-gns-geology` | New Zealand | GNS Science geology WMS | `https://maps.gns.cri.nz/geology/wms?SERVICE=WMS&REQUEST=GetCapabilities` | Capabilities parsed successfully; 57 layers, EPSG:2193/EPSG:3857 support. Good southern-hemisphere WMS and alternate-CRS source. |
| `wms-spain-catastro` | Spain | Spanish Cadastre WMS | `https://ovc.catastro.meh.es/Cartografia/WMS/ServidorWMS.aspx?SERVICE=WMS&REQUEST=GetCapabilities` | Capabilities parsed successfully; 13 cadastral layers, WMS 1.1.1. Intended to validate cadastral map context, older WMS version handling, and Spanish EPSG:4258/4326 behavior. |
| `wms-italy-cadastre` | Italy | Agenzia Entrate cadastral WMS | `https://wms.cartografia.agenziaentrate.gov.it/inspire/wms/ows01.php?service=WMS&request=GetCapabilities` | Capabilities parsed successfully; 11 cadastral layers, 2048 x 2048 max images, EPSG:6706/4258/3044/3045. Intended as an alternate cadastral WMS and CRS watch source. |
| `wms-uk-bgs-detailed-geology` | United Kingdom | British Geological Survey detailed geology WMS | `https://map.bgs.ac.uk/arcgis/services/BGS_Detailed_Geology/MapServer/WMSServer?service=WMS&request=GetCapabilities` | Capabilities parsed successfully; 6 ArcGIS WMS layers, CRS:84/EPSG:4326/EPSG:27700. Intended to validate ArcGIS WMS normalization and UK geology map context. |
| `wms-germany-topplusopen` | Germany | BKG TopPlusOpen WMS | `https://sgx.geodatenzentrum.de/wms_topplus_open?request=GetCapabilities&service=wms` | Existing Germany-wide official map context baseline. |
| `wms-hessen-imagery` | Germany / Hessen | Hessen orthophoto imagery WMS | `https://www.gds-srv.hessen.de/cgi-bin/lika-services/ogc-free-images.ows?language=ger&SERVICE=WMS&VERSION=1.3.0&REQUEST=GetCapabilities` | Existing high-resolution Hessen imagery baseline. |
| `wms-hessen-cadastral` | Germany / Hessen | Hessen cadastral map WMS | `https://www.geoportal.hessen.de/mapbender/php/wms.php?REQUEST=GetCapabilities&SERVICE=WMS&inspire=1&layer_id=54097&withChilds=1` | Existing cadastral/linework WMS baseline. |
| `wms-spain-pnoa` | Spain | IGN PNOA orthophoto WMS | `https://www.ign.es/wms-inspire/pnoa-ma?SERVICE=WMS&REQUEST=GetCapabilities` | Capabilities parsed successfully; good non-German orthophoto source with `OI.OrthoimageCoverage`. |
| `wms-france-geopf-raster` | France | IGN Geoplateforme raster WMS | `https://data.geopf.fr/wms-r/wms?SERVICE=WMS&REQUEST=GetCapabilities` | Capabilities parsed successfully; large layer catalogue and 5010 x 5010 max image limit. |
| `wms-geoserver-demo-naturalearth` | Global demo | GeoServer Natural Earth demo WMS | `https://ahocevar.com/geoserver/wms?SERVICE=WMS&REQUEST=GetCapabilities` | Capabilities parsed successfully. Good lightweight GeoServer WMS regression source. |
| `wms-norway-geonorge-topo` | Norway | Geonorge / Kartverket topographic WMS | `https://wms.geonorge.no/skwms1/wms.topo?service=WMS&request=GetCapabilities` | Capabilities parsed successfully; 226 layers and 8192 x 8192 max images. Good Nordic/national WMS provider test. |
| `wms-austria-vienna-open-data` | Austria / Vienna | Vienna Open Government Data WMS | `https://data.wien.gv.at/daten/geo?service=WMS&request=GetCapabilities&version=1.3.0` | Bounded `ADRESSENOGD` request returned a visible 512 x 512 PNG after applying WMS 1.3 EPSG:4326 latitude/longitude axis order. Also exercises the advertised HTTP operation URL quirk. |
| `wms-finland-nls-administrative-units` | Finland | National Land Survey INSPIRE administrative units WMS | `https://inspire-wms.maanmittauslaitos.fi/inspire-wms/AU/ows?service=wms&request=GetCapabilities` | Bounded `AU.AdministrativeUnit` request returned a valid 512 x 512 EPSG:3857 PNG. Capabilities advertise GetMap on a different official service host. |

### Terrain / WCS

| ID | Region | Source | URL | Current Use |
| --- | --- | --- | --- | --- |
| `terrain-global-skadi-fallback` | Global land areas | Built-in Skadi fallback | none | Built-in terrain fallback. Must stay fast and fail clearly for oversized contexts. |
| `wcs-germany-dgm200` | Germany | BKG DGM200 WCS | `https://sgx.geodatenzentrum.de/wcs_dgm200_inspire?SERVICE=WCS&REQUEST=GetCapabilities` | Capabilities parsed successfully. Official coarse Germany terrain baseline. |
| `terrain-hessen-dgm1` | Germany / Hessen | Hessen DGM1 WCS | `https://inspire-hessen.de/raster/dgm1/ows?REQUEST=GetCapabilities&SERVICE=WCS&VERSION=2.1.0` | Existing high-resolution Hessen terrain baseline. |
| `terrain-nrw-dgm` | Germany / NRW | NRW DGM WCS | `https://www.wcs.nrw.de/geobasis/wcs_nw_dgm?REQUEST=GetCapabilities&SERVICE=WCS` | Bounded `nw_dgm` request read a 100 x 100 raster with 10,000 valid elevations. DescribeCoverage native bounds now provide Spatial Context preloading when capabilities omit WGS84 bounds. |
| `terrain-netherlands-ahn` | Netherlands | PDOK AHN WCS | `https://service.pdok.nl/rws/ahn/wcs/v1_0?service=WCS&request=GetCapabilities` | Bounded `dtm_05m` request read a 200 x 200 raster with 40,000 valid elevations. EPSG:28992 bounds and data now transform explicitly for Spatial Context alignment. |
| `terrain-usgs-3dep` | United States | USGS 3DEP Elevation WCS | `https://elevation.nationalmap.gov/arcgis/services/3DEPElevation/ImageServer/WCSServer?SERVICE=WCS&REQUEST=GetCapabilities` | Bounded `DEP3Elevation` request read a 100 x 100 raster with 10,000 valid elevations. Validates ArcGIS WCS 2.0.1 multipart GeoTIFF extraction. |
| `terrain-canada-hrdem` | Canada | Natural Resources Canada HRDEM WCS | `https://datacube.services.geo.ca/ows/elevation?service=WCS&request=GetCapabilities` | Capabilities parsed successfully; WCS 1.1.1 with DSM/DTM coverages. Intended as a non-European terrain/WCS provider and WCS version compatibility source. |
| `wcs-rasdaman-demo` | Global / sample coverages | rasdaman OGC WCS demo | `https://ows.rasdaman.org/rasdaman/ows?SERVICE=WCS&REQUEST=GetCapabilities` | Capabilities parsed successfully. Good non-German WCS 2.1 provider/parser stress test; not a project-data recommendation. |

### LoD2 / CityGML Buildings

| ID | Region | Source | URL | Current Use |
| --- | --- | --- | --- | --- |
| `lod2-hessen-buildings` | Germany / Hessen | Hessen LoD2 WFS | `https://www.geoportal.hessen.de/mapbender/php/wfs.php?FEATURETYPE_ID=5589&INSPIRE=1&REQUEST=GetCapabilities&SERVICE=WFS&VERSION=2.0.0` | Existing LoD2 WFS baseline. Good for terrain/building alignment. |
| `lod2-sachsen-anhalt` | Germany / Sachsen-Anhalt | Sachsen-Anhalt LoD2 WFS | `https://www.geodatenportal.sachsen-anhalt.de/wss/service/ST_LVermGeo_LoD2_WFS/guest?request=GetCapabilities&service=WFS&version=2.0.0` | Bounded `ALKIS_LOD2_BU:BU.Building` request parsed successfully. Validates EPSG:4258 WFS axis order and inherited `srsDimension="3"` on enclosing GML geometry. |
| `citygml-ogc-building-lod2` | Format sample | OGC CityGML 2.0 LoD2 building sample | `https://schemas.opengis.net/citygml/examples/2.0/building/Building_LOD2-EPSG25832.gml` | Downloaded and parsed successfully with `lod2file`; 1 building / 7 surfaces. |
| `citygml-ogc-building-garage-lod2` | Format sample | OGC CityGML 2.0 LoD2 building + garage sample | `https://schemas.opengis.net/citygml/examples/2.0/building/Building_and_garage_LOD2-EPSG25832.gml` | Downloaded and parsed successfully with `lod2file`; 1 building / 13 surfaces. |
| `citygml-ogc-building-lod1` | Format sample | OGC CityGML 2.0 LoD1 building sample | `https://schemas.opengis.net/citygml/examples/2.0/building/Building_LOD1-EPSG25832.gml` | Downloaded and parsed successfully with `lod2file`; 1 building / 6 surfaces. Intended to validate that the LoD2 component can still parse simpler CityGML building geometry gracefully. |
| `cityjson-local` | User/project data | Bring-your-own CityJSON building file | none | `Load LoD2 Buildings` now accepts local `.json` / `.cityjson` files through the same `LoD2 Source` input. Sandbox smoke test parsed a simple CityJSON solid as 1 building / 6 surfaces. |
| `cityjson-denhaag-3dcities` | Netherlands / sample | 3D city model CityJSON sample | `https://3d.bk.tudelft.nl/opendata/cityjson/3dcities/v2.0/DenHaag_01.city.json` | Downloaded and parsed successfully with `lod2file`; 1,990 buildings / 16,209 surfaces. EPSG:7415 now uses the supported Dutch RD New horizontal transform for alignment. |
| `cityjson-rotterdam-delfshaven` | Netherlands / Rotterdam | Delfshaven CityJSON LoD2 sample | `https://3d.bk.tudelft.nl/opendata/cityjson/3dcities/v2.0/3-20-DELFSHAVEN.city.json` | Downloaded and parsed successfully with `lod2file`; 853 buildings / 15,482 surfaces. Tests textured CityJSON and explicit EPSG:28992 alignment. |

### GeoTIFF / Local Raster

| ID | Region | Source | URL | Current Use |
| --- | --- | --- | --- | --- |
| `geotiff-osgeo-byte` | Sample | OSGeo GDAL small projected GeoTIFF | `https://raw.githubusercontent.com/OSGeo/gdal/master/autotest/gcore/data/byte.tif` | Downloaded and read successfully; EPSG:26711, 20 x 20. |
| `geotiff-osgeo-rgbsmall` | Sample | OSGeo GDAL small WGS84 RGB GeoTIFF | `https://raw.githubusercontent.com/OSGeo/gdal/master/autotest/gcore/data/rgbsmall.tif` | Downloaded and read successfully; EPSG:4326, 50 x 50. |
| `geotiff-osgeo-utmsmall` | Sample | OSGeo GDAL small UTM-style GeoTIFF | `https://raw.githubusercontent.com/OSGeo/gdal/master/autotest/gcore/data/utmsmall.tif` | Downloaded and read successfully; EPSG:4267 metadata, 100 x 100. Intended as another lightweight raster metadata and bounds parser smoke test. |
| `geotiff-local` | User/project data | Bring-your-own GeoTIFF | none | Main GeoTIFF workflow remains local file input. |
| `terrainfile-local-geotiff-dem` | User/project data | Bring-your-own local GeoTIFF DEM | none | `Load Terrain` now accepts local `.tif/.tiff` DEM paths as a terrain source. Tested with the sandbox `terrainfile` command on `geotiff-osgeo-byte`. |
| `terrainfile-local-esri-ascii-grid` | User/project data | Bring-your-own Esri ASCII Grid DEM | none | `Load Terrain` now accepts local `.asc` terrain grids. The `Coverage` input can be used as an EPSG override such as `EPSG:25832`; otherwise the current Spatial Context SRS is assumed. |
| `terrainfile-local-xyz-csv-grid` | User/project data | Bring-your-own XYZ/CSV regular terrain grid | none | `Load Terrain` now accepts local `.xyz` and `.csv` regular point grids with x,y,z columns. The source is treated as a regular grid and aligned through the shared Spatial Context. |

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
| Geoscience Australia topographic ArcGIS WMS candidate | `404 Not Found` during WMS GetCapabilities probe | Keep out of examples until a stable public endpoint is confirmed. |
| Geoscience Australia NationalMap WMS candidate | `404 Not Found` during WMS GetCapabilities probe | `https://services.ga.gov.au/gis/services/NationalMap_Colour_Base/MapServer/WMSServer?request=GetCapabilities&service=WMS` was tested and should stay out until a stable public WMS endpoint is found. |
| Italy Agenzia Entrate cadastral WFS | `403 Forbidden` during WFS GetCapabilities probe | The cadastral WMS works, but `https://wfs.cartografia.agenziaentrate.gov.it/inspire/wfs/ows01.php?service=WFS&request=GetCapabilities` is not currently an open regression source. |
| Czech CUZK orthophoto ArcGIS WMS candidate | `400 Bad Request` during WMS GetCapabilities probe | `https://ags.cuzk.cz/arcgis/services/ORTOFOTO/MapServer/WMSServer?request=GetCapabilities&service=WMS` still does not work with current WMS normalization. |
| FEMA NFHL ArcGIS WMS candidate | SSL failure during WMS GetCapabilities probe | `https://hazards.fema.gov/gis/nfhl/services/public/NFHL/MapServer/WMSServer?request=GetCapabilities&service=WMS` should stay out until TLS/provider behavior is understood. |
| ISRIC SoilGrids WMS/WCS candidates | Empty capabilities in current sandbox probes | `https://maps.isric.org/mapserv?map=/map/soilgrids.map` returned no usable WMS/WCS layers through current readers; keep as a parser/provider watch item. |
| US Census TIGERweb State/County ArcGIS WMS candidate | `400 Bad Request` during WMS GetCapabilities probe | `https://tigerweb.geo.census.gov/arcgis/services/TIGERweb/State_County/MapServer/WMSServer?request=GetCapabilities&service=WMS` did not work with RhinoSpatial's current WMS request normalization. |
| Sweden Lantmateriet WMS candidates | `401 Unauthorized` during WMS GetCapabilities probe | Public-looking endpoints such as `https://maps.lantmateriet.se/topowebb/wms/v1?SERVICE=WMS&REQUEST=GetCapabilities` require authorization and should not be used as open regression sources. |
| LINZ Data Service WFS/WMS demo-key candidates | `401 Unauthorized` / `404 Not Found` during capabilities probes | LINZ remains interesting for New Zealand, but a real API key or confirmed open endpoint is needed before adding it to the active catalogue. |
| EOX WCS endpoint | SSL failure during WCS capabilities probe | `https://ows.eox.at/eoxserver/ows?SERVICE=WCS&REQUEST=GetCapabilities` remains useful as a provider-resilience watch item, but not a current regression source. |

## Format Gaps Worth Considering

These are not release blockers, but they are the most sensible future source-format candidates:

- **Local DEM / terrain files**
  `Load Terrain` now accepts local `.tif/.tiff` GeoTIFF DEM paths, `.asc` Esri ASCII Grid files, and regular `.xyz` / `.csv` point grids with x,y,z columns. Text-grid CRS is taken from the `Coverage` input when it looks like an EPSG code, otherwise from the current Spatial Context SRS.
- **CityJSON**
  Basic local CityJSON building import is now supported through `Load LoD2 Buildings`. More real CityJSON sources should be added to this test catalogue before calling it mature.
- **GeoPackage**
  Still a good candidate for local vector/project data because it can hold layers, CRS metadata, and mixed geometry more robustly than loose shapefiles. It remains unimplemented for now because robust GeoPackage support needs a deliberate SQLite/geometry-BLOB parser choice and should not be slipped in as a fragile partial reader.
- **Shapefile**
  Local Shapefile is now supported as a vector source through `Load WFS`, using the existing shared Spatial Context and geometry tree output. It is not treated as LoD2 building import; many municipal Shapefiles are 2D footprints or thematic vector layers rather than true LoD2 geometry.
- **Local GeoJSON**
  Local `.geojson` and GeoJSON `.json` files are now supported as vector sources through `Load WFS`. This is intentionally simple: FeatureCollection geometry is treated like WFS/OGC vector geometry, defaulting to EPSG:4326 unless the file advertises an older named CRS.
- **OGC API Features**
  Basic GeoJSON OGC API Features item/collection URLs are now supported through the vector loader path. They are not treated as WFS internally; RhinoSpatial requests GeoJSON items with the Spatial Context WGS84 bbox and transforms them into the shared context.
- **Cloud Optimized GeoTIFF**
  Still unimplemented. COG support would be a remote-raster workflow, not just a local GeoTIFF variation, and should wait until range-request/cache behavior is designed.
