// Stepped Streets, the plan gallery: renders a neighbourhood plan to a PNG over a hillshade. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using SteppedStreets.Core.Neighbourhood;
using Unity.Mathematics;

namespace SteppedStreets.Tests.Neighbourhood
{
    /// <summary>
    /// Draws a plan the way the preview will: the planned ground as a hillshade with 5 m contours, cut and fill tinted, lots
    /// coloured by zone (level cells stronger), roads coloured by grade, trees as dots, the drawn area in white.
    /// </summary>
    public static class Gallery
    {
        public static void Render(NeighbourhoodRequest request, NeighbourhoodPlan plan, string path, float metresPerPixel = 1f)
        {
            float2 minimum = float.MaxValue, maximum = float.MinValue;
            foreach (var corner in request.Boundary) { minimum = math.min(minimum, corner); maximum = math.max(maximum, corner); }
            foreach (var road in plan.Roads) foreach (var point in road.Points) { minimum = math.min(minimum, point.xz); maximum = math.max(maximum, point.xz); }
            minimum -= 60f;
            maximum += 60f;
            int width = (int)math.ceil((maximum.x - minimum.x) / metresPerPixel);
            int height = (int)math.ceil((maximum.y - minimum.y) / metresPerPixel);
            var image = new Canvas(width, height, minimum, metresPerPixel);

            Func<float2, float> final = point => plan.Ground != null ? plan.Ground.HeightAt(point) : request.GroundHeight(point);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float2 world = image.World(x, y);
                    float h = final(world);
                    float dx = final(world + new float2(1f, 0f)) - final(world - new float2(1f, 0f));
                    float dz = final(world + new float2(0f, 1f)) - final(world - new float2(0f, 1f));
                    var normal = math.normalize(new float3(-dx * 0.5f, 1f, -dz * 0.5f));
                    float shade = math.saturate(math.dot(normal, math.normalize(new float3(-1f, 1.6f, 1f))) * 1.1f);
                    float slope = math.length(new float2(dx, dz)) * 0.5f;
                    bool inside = Core.Terrain.Poly.Contains(request.Boundary, world);
                    float3 colour = slope > request.SteepestBuildable ? new float3(0.42f, 0.5f, 0.3f) : new float3(0.55f, 0.68f, 0.38f);
                    if (!inside) colour = math.lerp(colour, new float3(0.6f), 0.45f);
                    colour *= 0.35f + 0.75f * shade;
                    if (request.Blocked != null && request.Blocked(world)) colour = new float3(0.3f, 0.45f, 0.75f);
                    float change = plan.Ground != null ? plan.Ground.ChangeAt(world) : 0f;
                    if (math.abs(change) > 0.15f)
                        colour = math.lerp(colour, change < 0f ? new float3(0.62f, 0.42f, 0.3f) : new float3(0.85f, 0.78f, 0.55f),
                            math.saturate(math.abs(change) / 3f) * 0.6f + 0.15f);
                    // 5 m contours, so streets can be seen following them.
                    float band = h / 5f;
                    if (math.abs(band - math.round(band)) < 0.06f * math.max(1f, slope * 8f)) colour *= 0.8f;
                    image.Set(x, y, colour);
                }

            foreach (var road in request.ExistingRoads ?? new ExistingRoad[0])
                for (int point = 1; point < road.Points.Length; point++)
                    image.Line(road.Points[point - 1].xz, road.Points[point].xz, road.HalfWidth, new float3(0.25f), 1f);

            foreach (var lot in plan.Lots)
            {
                var colour = ZoneColour(lot.Zone);
                var corners = new float2[4];
                for (int corner = 0; corner < 4; corner++) corners[corner] = lot.Pad.Corners[corner].xz;
                image.Polygon(corners, colour, 0.35f);
                foreach (var cell in lot.LevelCells)
                {
                    var quad = new[]
                    {
                        lot.Pad.PointAt(cell.x + 0.1f, cell.y + 0.1f), lot.Pad.PointAt(cell.x + 0.9f, cell.y + 0.1f),
                        lot.Pad.PointAt(cell.x + 0.9f, cell.y + 0.9f), lot.Pad.PointAt(cell.x + 0.1f, cell.y + 0.9f),
                    };
                    image.Polygon(quad, colour, 0.75f);
                }
                for (int corner = 0; corner < 4; corner++)
                    image.Line(corners[corner], corners[(corner + 1) % 4], 0.35f, colour * 0.55f, 1f);
            }

            foreach (var tree in plan.Trees)
                image.Disc(tree.Position.xz, 1.6f, tree.Kind == TreeKind.Conifer ? new float3(0.12f, 0.3f, 0.15f) : new float3(0.2f, 0.42f, 0.16f), 0.9f);

            foreach (var wall in plan.Walls) image.Line(wall.Start.xz, wall.End.xz, 0.6f, new float3(0.9f, 0.9f, 0.85f), 1f);

            foreach (var road in plan.Roads)
                for (int point = 1; point < road.Points.Length; point++)
                {
                    float run = math.distance(road.Points[point].xz, road.Points[point - 1].xz);
                    float grade = run > 0.01f ? math.abs(road.Points[point].y - road.Points[point - 1].y) / run : 0f;
                    image.Line(road.Points[point - 1].xz, road.Points[point].xz, request.RoadHalfWidth, GradeColour(grade, request.MaxRoadGrade), 1f);
                }
            foreach (var node in plan.Nodes)
            {
                if (node.Degree >= 3) image.Disc(node.Position.xz, 2.5f, new float3(1f), 1f);
                else if (node.DeadEnd) image.Disc(node.Position.xz, request.RoadHalfWidth + 1.5f, new float3(0.35f, 0.35f, 0.4f), 1f);
                if (node.ExistingRoadId != 0) image.Disc(node.Position.xz, 4f, new float3(1f, 0.2f, 0.8f), 1f);
            }
            for (int corner = 0; corner < request.Boundary.Length; corner++)
                image.Line(request.Boundary[corner], request.Boundary[(corner + 1) % request.Boundary.Length], 1f, new float3(1f), 0.9f);

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            image.Save(path);
        }

        private static float3 GradeColour(float grade, float maximum)
        {
            if (grade <= 0.03f) return new float3(0.75f, 0.78f, 0.82f);
            if (grade <= 0.06f) return new float3(0.95f, 0.85f, 0.3f);
            if (grade <= maximum + 0.003f) return new float3(1f, 0.55f, 0.15f);
            return new float3(0.95f, 0.1f, 0.1f);
        }

        public static float3 ZoneColour(string zone)
        {
            string name = (zone ?? "").ToLowerInvariant();
            if (name.Contains("commercial")) return new float3(0.25f, 0.55f, 0.95f);
            if (name.Contains("office")) return new float3(0.65f, 0.4f, 0.9f);
            if (name.Contains("industrial")) return new float3(0.95f, 0.8f, 0.2f);
            if (name.Contains("medium") || name.Contains("row")) return new float3(0.2f, 0.75f, 0.35f);
            if (name.Contains("high")) return new float3(0.1f, 0.55f, 0.25f);
            return new float3(0.55f, 0.95f, 0.45f);
        }

        private sealed class Canvas
        {
            private readonly int width, height;
            private readonly float2 origin;
            private readonly float scale;
            private readonly float3[] pixels;

            public Canvas(int width, int height, float2 origin, float scale)
            {
                this.width = width; this.height = height; this.origin = origin; this.scale = scale;
                pixels = new float3[width * height];
            }

            // Image rows run downwards; world z runs up the picture.
            public float2 World(int x, int y) => origin + new float2(x + 0.5f, height - 1 - y + 0.5f) * scale;
            private float2 Pixel(float2 world) => new float2((world.x - origin.x) / scale, height - 1 - (world.y - origin.y) / scale);

            public void Set(int x, int y, float3 colour) => pixels[y * width + x] = colour;

            private void Blend(int x, int y, float3 colour, float alpha)
            {
                if (x < 0 || y < 0 || x >= width || y >= height) return;
                int index = y * width + x;
                pixels[index] = math.lerp(pixels[index], colour, alpha);
            }

            public void Line(float2 start, float2 end, float halfWidth, float3 colour, float alpha)
            {
                float2 a = Pixel(start), b = Pixel(end);
                float radius = math.max(0.5f, halfWidth / scale);
                int x0 = (int)math.floor(math.min(a.x, b.x) - radius), x1 = (int)math.ceil(math.max(a.x, b.x) + radius);
                int y0 = (int)math.floor(math.min(a.y, b.y) - radius), y1 = (int)math.ceil(math.max(a.y, b.y) + radius);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float2 p = new float2(x + 0.5f, y + 0.5f), ab = b - a;
                        float t = math.lengthsq(ab) > 1e-6f ? math.saturate(math.dot(p - a, ab) / math.lengthsq(ab)) : 0f;
                        if (math.distance(p, a + ab * t) <= radius) Blend(x, y, colour, alpha);
                    }
            }

            public void Disc(float2 centre, float radius, float3 colour, float alpha) => Line(centre, centre, radius, colour, alpha);

            public void Polygon(float2[] ring, float3 colour, float alpha)
            {
                var points = new float2[ring.Length];
                float2 low = float.MaxValue, high = float.MinValue;
                for (int index = 0; index < ring.Length; index++) { points[index] = Pixel(ring[index]); low = math.min(low, points[index]); high = math.max(high, points[index]); }
                for (int y = (int)math.floor(low.y); y <= (int)math.ceil(high.y); y++)
                    for (int x = (int)math.floor(low.x); x <= (int)math.ceil(high.x); x++)
                        if (Core.Terrain.Poly.Contains(points, new float2(x + 0.5f, y + 0.5f))) Blend(x, y, colour, alpha);
            }

            public void Save(string path)
            {
                var raw = new byte[height * (width * 3 + 1)];
                for (int y = 0; y < height; y++)
                {
                    int row = y * (width * 3 + 1);
                    raw[row] = 0;
                    for (int x = 0; x < width; x++)
                    {
                        var colour = math.saturate(pixels[y * width + x]) * 255f;
                        raw[row + 1 + x * 3] = (byte)colour.x;
                        raw[row + 2 + x * 3] = (byte)colour.y;
                        raw[row + 3 + x * 3] = (byte)colour.z;
                    }
                }
                using var file = File.Create(path);
                file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
                var header = new byte[13];
                WriteBigEndian(header, 0, (uint)width);
                WriteBigEndian(header, 4, (uint)height);
                header[8] = 8; header[9] = 2;
                Chunk(file, "IHDR", header);
                using (var compressed = new MemoryStream())
                {
                    using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true)) zlib.Write(raw);
                    Chunk(file, "IDAT", compressed.ToArray());
                }
                Chunk(file, "IEND", Array.Empty<byte>());
            }

            private static void Chunk(Stream stream, string type, byte[] data)
            {
                var length = new byte[4];
                WriteBigEndian(length, 0, (uint)data.Length);
                stream.Write(length);
                var typed = new byte[4 + data.Length];
                for (int index = 0; index < 4; index++) typed[index] = (byte)type[index];
                Array.Copy(data, 0, typed, 4, data.Length);
                stream.Write(typed);
                var crc = new byte[4];
                WriteBigEndian(crc, 0, Crc(typed));
                stream.Write(crc);
            }

            private static void WriteBigEndian(byte[] buffer, int offset, uint value)
            {
                buffer[offset] = (byte)(value >> 24); buffer[offset + 1] = (byte)(value >> 16);
                buffer[offset + 2] = (byte)(value >> 8); buffer[offset + 3] = (byte)value;
            }

            private static uint Crc(byte[] data)
            {
                uint crc = 0xFFFFFFFF;
                foreach (byte value in data)
                {
                    crc ^= value;
                    for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
                }
                return ~crc;
            }
        }
    }
}
