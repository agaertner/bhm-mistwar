using Gw2Sharp.Models;
using Gw2Sharp.WebApi.V2.Models;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Point = Microsoft.Xna.Framework.Point;
using Rectangle = Gw2Sharp.WebApi.V2.Models.Rectangle;
namespace Nekres.Mistwar {
    internal static class MapUtil {

        private const int MAX_ZOOM  = 7;
        private const int TILE_SIZE = 256;
        private const int PADDING   = 0;
        public static async Task BuildMap(Map map, string filePath, bool removeBackground = false, IProgress<string> progress = null)
        {
            if (map == null) {
                return;
            }

            var area = map.ContinentRect;

            var tileArea = GetAreaTileList(area);

            // area
            var topLeftPx = area.TopLeft;
            var rightBottomPx = area.BottomRight;
            var pxDelta = new Point((int)(rightBottomPx.X - topLeftPx.X), (int)(rightBottomPx.Y - topLeftPx.Y));

            var bmpDestination = new Bitmap(pxDelta.X + PADDING * 2, pxDelta.Y + PADDING * 2);
            using (var gfx = Graphics.FromImage(bmpDestination))
            {
                gfx.CompositingMode = CompositingMode.SourceOver;

                // get tiles & combine into one
                foreach (var p in tileArea)
                {
                    progress?.Report($"Downloading {map.Name.Trim()} ({map.Id})... {tileArea.IndexOf(p) / (float)(tileArea.Count - 1) * 100:N0}%");

                    using var tile = await GetTileImage(0, map.ContinentId, map.DefaultFloor, p.X, p.Y, MAX_ZOOM);
                    if (tile == null) {
                        continue;
                    }
                    var x = (long)(p.X * tile.Width  - topLeftPx.X + PADDING);
                    var y = (long)(p.Y * tile.Height - topLeftPx.Y + PADDING);
                    gfx.DrawImage(tile, x, y, tile.Width, tile.Height);
                }
                gfx.Flush();

                if (removeBackground)
                {
                    var mapExp = await MistwarModule.ModuleInstance.Resources.GetMapExpanded(map, map.DefaultFloor);
                    if (mapExp != null && mapExp.Sectors.Count > 0) {
                        var polygonPath = new GraphicsPath();
                        polygonPath.FillMode = FillMode.Alternate;
                        foreach (var sector in mapExp.Sectors.Values)
                        {
                            var bbox = sector.Bounds.Select(coord => Refit(coord, topLeftPx, PADDING)).ToArray();
                            polygonPath.AddPolygon(bbox);
                        }
                        // remove any pixels not inside a sector
                        var region = new Region();
                        region.MakeInfinite();
                        region.Exclude(polygonPath);
                        gfx.CompositingMode = CompositingMode.SourceCopy;
                        gfx.FillRegion(Brushes.Transparent, region);
                    }
                }
            }

            //bmpDestination.MakeGrayscale();

            bmpDestination.Save(filePath, ImageFormat.Png);
            bmpDestination.Dispose();
        }

        public static System.Drawing.Point Refit(Coordinates2 value, Coordinates2 destTopLeft, int padding = PADDING)
        {
            var node = new Coordinates2(value.X / TILE_SIZE, value.Y / TILE_SIZE);
            var x    = (int)(node.X * TILE_SIZE - destTopLeft.X + padding);
            var y    = (int)(node.Y * TILE_SIZE - destTopLeft.Y + padding);
            return new System.Drawing.Point(x, y);
        }

        private static Point FromPixelToTileXy(Coordinates2 p, int zoom = MAX_ZOOM)
        {
            return new Point((int)(p.X / TILE_SIZE), (int)(p.Y / TILE_SIZE));
        }

        private static List<Point> GetAreaTileList(Rectangle rect)
        {
            var topLeft = FromPixelToTileXy(rect.TopLeft);
            var rightBottom = FromPixelToTileXy(rect.BottomRight);

            int x = Math.Max(0, topLeft.X);
            int toX = rightBottom.X;
            int y0 = Math.Max(0, topLeft.Y);
            int toY = rightBottom.Y;

            var list = new List<Point>((toX - x + 1) * (toY - y0 + 1));

            for (; x <= toX; x++)
            {
                for (int y = y0; y <= toY; y++)
                {
                    list.Add(new Point(x, y));
                }
            }

            return list;
        }

        private static async Task<Bitmap> GetTileImage(int dnsAlias, int continentId, int floor, int x, int y, int zoom) {
            if (zoom < 0 || zoom > MAX_ZOOM)
                throw new ArgumentOutOfRangeException(nameof(zoom));

            var dns = (dnsAlias >= 1 && dnsAlias <= 4) ? dnsAlias.ToString() : "1";
            var url = $"https://tiles{dns}.guildwars2.com/{continentId}/{floor}/{zoom}/{x}/{y}.jpg";
            try {
                using var client         = new HttpClient();
                using var responseStream = await client.GetStreamAsync(url);
                using var ms             = new MemoryStream();
                await responseStream.CopyToAsync(ms);
                ms.Position = 0;
                return new Bitmap(ms);
            } catch (Exception) {
                return null;
            }
        }
    }
}
