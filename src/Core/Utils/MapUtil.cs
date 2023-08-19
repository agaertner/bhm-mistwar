using Blish_HUD;
using Blish_HUD.Extended;
using Gw2Sharp.Models;
using Gw2Sharp.WebApi.V2.Models;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading.Tasks;
using Point = Microsoft.Xna.Framework.Point;
using Rectangle = Gw2Sharp.WebApi.V2.Models.Rectangle;
namespace Nekres.Mistwar {
    internal static class MapUtil
    {
        public static async Task BuildMap(Map map, string filePath, bool removeBackground = false, IProgress<string> progress = null)
        {
            if (map == null) {
                return;
            }

            var area = map.ContinentRect;

            var zoom = 6;
            var padding = 0;

            var tileArea = GetAreaTileList(area);

            // area
            var topLeftPx = area.TopLeft;
            var rightBottomPx = area.BottomRight;
            var pxDelta = new Point((int)(rightBottomPx.X - topLeftPx.X), (int)(rightBottomPx.Y - topLeftPx.Y));

            var bmpDestination = new Bitmap(pxDelta.X + padding * 2, pxDelta.Y + padding * 2);
            using (var gfx = Graphics.FromImage(bmpDestination))
            {
                gfx.CompositingMode = CompositingMode.SourceOver;

                // get tiles & combine into one
                foreach (var p in tileArea)
                {
                    progress?.Report($"Downloading {map.Name.Trim()} ({map.Id})... {tileArea.IndexOf(p) / (float)(tileArea.Count - 1) * 100:N0}%");

                    var tile = await GetTileImage(0, map.ContinentId, map.DefaultFloor, p.X, p.Y, zoom);
                    if (tile == null) {
                        continue;
                    }

                    using (tile)
                    {
                        var x = (long)(p.X * tile.Width - topLeftPx.X + padding);
                        var y = (long)(p.Y * tile.Height - topLeftPx.Y + padding);
                        gfx.DrawImage(tile, x, y, tile.Width, tile.Height);
                    }
                }
                gfx.Flush();

                if (removeBackground)
                {
                    var mapExp = await MistwarModule.ModuleInstance.Resources.GetMapExpanded(map, map.DefaultFloor);

                    var polygonPath = new GraphicsPath();
                    polygonPath.FillMode = FillMode.Alternate;
                    foreach (var sector in mapExp.Sectors.Values)
                    {
                        var bbox = sector.Bounds.Select(coord => Refit(coord, topLeftPx, padding)).ToArray();
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

            //bmpDestination.MakeGrayscale();

            bmpDestination.Save(filePath, ImageFormat.Png);
            bmpDestination.Dispose();
        }

        public static System.Drawing.Point Refit(Coordinates2 value, Coordinates2 destTopLeft, int padding = 0, int tileSize = 256)
        {
            var node = new Coordinates2(value.X / tileSize, value.Y / tileSize);
            var x = (int)(node.X * tileSize - destTopLeft.X + padding);
            var y = (int)(node.Y * tileSize - destTopLeft.Y + padding);
            return new System.Drawing.Point(x, y);
        }

        private static Point FromPixelToTileXy(Coordinates2 p, int zoom = 8)
        {
            var tileSize = zoom * 32;
            return new Point((int)(p.X / tileSize), (int)(p.Y / tileSize));
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

        private static async Task<Bitmap> GetTileImage(int dnsAlias, int continentId, int floor, int x, int y, int zoom = 6)
        {
            if (zoom < 0 || zoom > 7) {
                return null;
            }

            var       dns      = dnsAlias > 0 && dnsAlias < 5 ? dnsAlias.ToString() : string.Empty;
            var       request        = System.Net.WebRequest.Create($"https://tiles{dns}.guildwars2.com/{continentId}/{floor}/{zoom}/{x}/{y}.jpg");
            using var response       = await request.GetResponseAsync();
            using var responseStream = response.GetResponseStream();
            if (responseStream == null) {
                return null;
            }
            return new Bitmap(responseStream);
        }
    }
}
