using System;
using System.IO;
using System.Linq;
using BitMiracle.LibTiff.Classic;

namespace RhinoSpatial.Core
{
    public static class GeoTiffReader
    {
        private const ushort GeographicTypeGeoKey = 2048;
        private const ushort ProjectedCSTypeGeoKey = 3072;

        public static GeoReferencedRasterInfo ReadImageInfo(string filePath) => ReadImageInfo(filePath,null);

        public static GeoReferencedRasterInfo ReadImageInfo(string filePath,string? declaredFallbackSrs)
        {
            if (new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase))
            {
                using var stream=File.OpenRead(filePath);
                var info=StbImageNext.ImageInfo.FromStream(stream);
                if(info.Width<=0 || info.Height<=0)throw new InvalidDataException("Invalid raster image dimensions.");
                var sidecar=RasterSidecars.Read(filePath);
                return new GeoReferencedRasterInfo(filePath,sidecar.Srs,info.Width,info.Height,sidecar.Transform.Bounds(info.Width,info.Height),new FileInfo(filePath).Length) { GeoTransform=sidecar.Transform };
            }
            TiffWarningSuppression.EnsureInstalled();
            using var tiff=Tiff.Open(filePath,"r");
            if(tiff is null) throw new InvalidOperationException("Failed to open raster: "+filePath);
            var width=tiff.GetField(TiffTag.IMAGEWIDTH)?[0].ToInt()??0;
            var height=tiff.GetField(TiffTag.IMAGELENGTH)?[0].ToInt()??0;
            if(width<=0 || height<=0) throw new InvalidDataException("Invalid raster dimensions.");
            var srs=ReadSrsName(tiff);
            var transform=ReadTransform(tiff);
            transform ??= RasterSidecars.ReadTransform(filePath);
            if(string.IsNullOrWhiteSpace(srs))srs=string.IsNullOrWhiteSpace(declaredFallbackSrs)?RasterSidecars.ReadSrs(filePath):declaredFallbackSrs;
            transform.Validate();
            return new GeoReferencedRasterInfo(filePath,srs,width,height,transform.Bounds(width,height),new FileInfo(filePath).Length) { GeoTransform=transform };
        }

        private static RasterGeoTransform? ReadTransform(Tiff tiff)
        {
            RasterGeoTransform? g=null;
            var matrix=TryReadDoubleArray(tiff,TiffTag.GEOTIFF_MODELTRANSFORMATIONTAG);
            if(matrix is { Length: >=16 })
            {
                if(matrix.Any(v=>!double.IsFinite(v)) || matrix[12]!=0 || matrix[13]!=0 || matrix[15]!=1)
                    throw new InvalidDataException("Unsupported GeoTIFF projective transformation.");
                g=new(matrix[0],matrix[1],matrix[3],matrix[4],matrix[5],matrix[7]);
            }
            else
            {
                var scale=TryReadDoubleArray(tiff,TiffTag.GEOTIFF_MODELPIXELSCALETAG);
                var tie=TryReadDoubleArray(tiff,TiffTag.GEOTIFF_MODELTIEPOINTTAG);
                if(scale is {Length:>=2} && tie is {Length:>=6}) g=new(scale[0],0,tie[3]-tie[0]*scale[0],0,-scale[1],tie[4]+tie[1]*scale[1]);
            }
            if(g is null) return null;
            var keys=TryReadUShortArray(tiff,TiffTag.GEOTIFF_GEOKEYDIRECTORYTAG);
            if(keys is not null)
                for(var i=4;i+3<Math.Min(keys.Length,4+keys[3]*4);i+=4)
                    if(keys[i]==1025 && keys[i+1]==0 && keys[i+3]==2)
                        g=g with { C=g.C-(g.A+g.B)/2,F=g.F-(g.D+g.E)/2 };
            g.Validate(); return g;
        }

        private static string ReadSrsName(Tiff tiff)
        {
            var geoKeys = TryReadUShortArray(tiff, TiffTag.GEOTIFF_GEOKEYDIRECTORYTAG);
            if (geoKeys is not { Length: >= 8 })
            {
                return string.Empty;
            }

            foreach(var requested in new[]{ProjectedCSTypeGeoKey,GeographicTypeGeoKey})
                for(var offset=4;offset+3<Math.Min(geoKeys.Length,4+geoKeys[3]*4);offset+=4)
                    if(geoKeys[offset]==requested && geoKeys[offset+1]==0 && geoKeys[offset+2]==1 && geoKeys[offset+3]>0 && geoKeys[offset+3]!=32767)
                        return "EPSG:"+geoKeys[offset+3];
            return string.Empty;
        }

        private static double[]? TryReadDoubleArray(Tiff tiff, TiffTag tag)
        {
            var field = tiff.GetField(tag);
            if (field is null || field.Length == 0)
            {
                return null;
            }

            for (var index = field.Length - 1; index >= 0; index--)
            {
                try
                {
                    var values = field[index].ToDoubleArray();
                    if (values is { Length: > 0 })
                    {
                        return values;
                    }
                }
                catch
                {
                }

                try
                {
                    return new[] { field[index].ToDouble() };
                }
                catch
                {
                }
            }

            return null;
        }

        private static ushort[]? TryReadUShortArray(Tiff tiff, TiffTag tag)
        {
            var field = tiff.GetField(tag);
            if (field is null || field.Length == 0)
            {
                return null;
            }

            for (var index = field.Length - 1; index >= 0; index--)
            {
                try
                {
                    var values = field[index].ToUShortArray();
                    if (values is { Length: > 0 })
                    {
                        return values;
                    }
                }
                catch
                {
                }

                try
                {
                    return new[] { field[index].ToUShort() };
                }
                catch
                {
                }
            }

            return null;
        }
    }
}
