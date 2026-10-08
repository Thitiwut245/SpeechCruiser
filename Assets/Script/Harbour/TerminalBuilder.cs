using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A container terminal built in code, joined to the land: a long concrete quay wall with the
/// berth (Quay: fenders, bollards, markings, the working crane), a second crane parked along
/// the quay, a road behind the cranes, a container yard of coloured stacks with a yard gantry,
/// warehouses at the back, an office with a control tower, and light masts. Yard trucks drive
/// up and down the road. One mesh for the static parts, so it costs little to draw.
/// Local layout, from the quay face eastward (u) and along the quay (v, from the berth):
///   u 0..12 crane lane, 13..18 road, 20..44 container yard, 46..55 warehouses.
/// </summary>
public static class TerminalBuilder
{
    public const float Depth = 56f;    // quay face to the back of the terminal
    public const float DeckY = 2.4f;   // the paved yard's height (the quay's top)

    // Materials, in submesh order.
    private enum Mat { Concrete, Wall, Asphalt, Paint, Shed, Roof, Office, Glass, Mast, BoxRed, BoxBlue, BoxGreen, BoxOrange, BoxWhite, BoxTeal, Yellow }
    private static readonly Color[] Colours =
    {
        new Color(0.56f, 0.56f, 0.54f), // yard concrete
        new Color(0.45f, 0.44f, 0.42f), // quay wall
        new Color(0.20f, 0.21f, 0.23f), // road
        new Color(0.92f, 0.92f, 0.88f), // road paint
        new Color(0.78f, 0.80f, 0.82f), // warehouse walls
        new Color(0.22f, 0.36f, 0.52f), // warehouse roofs
        new Color(0.90f, 0.88f, 0.82f), // office
        new Color(0.14f, 0.22f, 0.30f), // windows
        new Color(0.60f, 0.62f, 0.64f), // light masts
        new Color(0.70f, 0.20f, 0.16f), // containers
        new Color(0.16f, 0.32f, 0.58f),
        new Color(0.18f, 0.48f, 0.30f),
        new Color(0.88f, 0.48f, 0.14f),
        new Color(0.88f, 0.88f, 0.86f),
        new Color(0.16f, 0.58f, 0.60f),
        new Color(0.95f, 0.75f, 0.12f), // yard gantry
    };
    private static Material[] _materials;

    /// <summary>The shared MeshKit, spoken to in this file's materials.</summary>
    private readonly struct Paint
    {
        public readonly MeshKit Kit;
        public Paint(MeshKit kit) { Kit = kit; }
        public void Box(Mat m, Vector3 c, Vector3 s) => Kit.Box((int)m, c, s);
        public void Gable(Mat m, Vector3 c, Vector3 s) => Kit.Gable((int)m, c, s);
    }

    /// <summary>
    /// Builds a terminal whose quay faces west at <paramref name="faceX"/>, for a ship lying at
    /// <paramref name="berth"/>. It runs <paramref name="south"/> metres along the quay behind the
    /// berth and <paramref name="north"/> ahead of it. Returns its footprint (x/z).
    /// </summary>
    public static Rect Build(Transform parent, string name, Vector3 berth, float faceX, float south, float north,
                             bool destination, int seed, out Quay quay)
    {
        var rng = new System.Random(seed);
        var root = new GameObject(name).transform;
        root.SetParent(parent, false);
        var k = new Paint(new MeshKit(Colours.Length));
        float z0 = berth.z - south, z1 = berth.z + north, len = z1 - z0, mid = (z0 + z1) * 0.5f;
        float U(float u) => faceX + u;

        // The yard slab, and the quay wall down into the water along its edge.
        k.Box(Mat.Concrete, new Vector3(U(Depth * 0.5f), DeckY - 1.5f, mid), new Vector3(Depth, 3f, len));
        k.Box(Mat.Wall, new Vector3(U(1.5f), (DeckY - 7f) * 0.5f, mid), new Vector3(3f, DeckY + 7f, len));
        k.Box(Mat.Paint, new Vector3(U(0.15f), DeckY + 0.02f, mid), new Vector3(0.3f, 0.04f, len)); // edge line
        // Crane rails.
        foreach (float u in new[] { 1.4f, 10.4f })
            k.Box(Mat.Mast, new Vector3(U(u), DeckY + 0.03f, mid), new Vector3(0.35f, 0.06f, len - 2f));

        // The road behind the cranes, with dashes down the middle.
        k.Box(Mat.Asphalt, new Vector3(U(15.5f), DeckY + 0.02f, mid), new Vector3(5.5f, 0.04f, len));
        for (float z = z0 + 2f; z < z1 - 2f; z += 6f)
            k.Box(Mat.Paint, new Vector3(U(15.5f), DeckY + 0.05f, z), new Vector3(0.2f, 0.02f, 2.6f));

        // The container yard: blocks of stacks, aisles between them.
        var boxes = new[] { Mat.BoxRed, Mat.BoxBlue, Mat.BoxGreen, Mat.BoxOrange, Mat.BoxWhite, Mat.BoxTeal };
        for (float u = 20.5f; u < 44f; u += 2.2f)
        {
            if (Mathf.Abs(u - 32f) < 1.2f) continue; // aisle for the yard gantry's lane
            for (float z = z0 + 4f; z < z1 - 4f; z += 3.4f)
            {
                if (Mathf.Repeat(z - z0, 24f) > 20f) continue; // cross aisles
                int high = rng.Next(0, 5);
                for (int h = 0; h < high; h++)
                    k.Box(boxes[rng.Next(boxes.Length)], new Vector3(U(u), DeckY + 0.62f + h * 1.3f, z),
                          new Vector3(1.55f, 1.24f, 3.1f));
            }
        }
        // A yard gantry over the stacks.
        float gz = z0 + len * (0.3f + 0.4f * (float)rng.NextDouble());
        foreach (float u in new[] { 21f, 43f })
            foreach (float dz in new[] { -1.8f, 1.8f })
                k.Box(Mat.Yellow, new Vector3(U(u), DeckY + 4f, gz + dz), new Vector3(0.5f, 8f, 0.5f));
        foreach (float dz in new[] { -1.8f, 1.8f })
            k.Box(Mat.Yellow, new Vector3(U(32f), DeckY + 8.2f, gz + dz), new Vector3(23f, 0.6f, 0.5f));
        k.Box(Mat.Office, new Vector3(U(30f), DeckY + 7.6f, gz), new Vector3(1.6f, 1.2f, 4f));

        // Warehouses at the back: long sheds with pitched roofs.
        for (float z = z0 + 6f; z + 18f < z1; z += 24f)
        {
            k.Box(Mat.Shed, new Vector3(U(50.5f), DeckY + 3f, z + 9f), new Vector3(9f, 6f, 18f));
            k.Gable(Mat.Roof, new Vector3(U(50.5f), DeckY + 6f, z + 9f), new Vector3(9.6f, 2.4f, 18.6f));
            k.Box(Mat.Glass, new Vector3(U(45.95f), DeckY + 2f, z + 9f), new Vector3(0.1f, 3.6f, 5f)); // roller door
        }

        // The office and control tower by the gate (the land end of the quay).
        float gateZ = destination ? z1 - 10f : z0 + 10f;
        k.Box(Mat.Office, new Vector3(U(17f), DeckY + 3f, gateZ), new Vector3(8f, 6f, 10f));
        k.Box(Mat.Glass, new Vector3(U(12.95f), DeckY + 3.4f, gateZ), new Vector3(0.1f, 1.2f, 8f));
        k.Box(Mat.Office, new Vector3(U(19f), DeckY + 9f, gateZ + (destination ? 2f : -2f)), new Vector3(3.6f, 18f, 3.6f));
        k.Box(Mat.Glass, new Vector3(U(19f), DeckY + 17f, gateZ + (destination ? 2f : -2f)), new Vector3(4.6f, 2.4f, 4.6f));
        k.Box(Mat.Office, new Vector3(U(19f), DeckY + 18.5f, gateZ + (destination ? 2f : -2f)), new Vector3(5f, 0.4f, 5f));

        // Light masts along the road.
        for (float z = z0 + 8f; z < z1 - 4f; z += 18f)
        {
            k.Box(Mat.Mast, new Vector3(U(18.6f), DeckY + 7f, z), new Vector3(0.3f, 14f, 0.3f));
            k.Box(Mat.Paint, new Vector3(U(18.6f), DeckY + 14.2f, z), new Vector3(1.6f, 0.4f, 0.8f));
        }

        k.Kit.Finish(root, "Terminal", Materials());

        // The berth: fenders, bollards, markings and the working crane, plus a second crane
        // parked further along the quay.
        var footprint = Rect.MinMaxRect(faceX, z0, faceX + Depth, z1);
        quay = Quay.Build(root, footprint, berth, faceX, DeckY, destination);
        var parked = QuayCrane.Build(root, berth + Vector3.forward * (destination ? -26f : 22f), faceX, DeckY);
        parked.name = "ParkedCrane";

        // Yard trucks up and down the road.
        for (int i = 0; i < 2; i++)
            YardTruck.Patrol(root, new Vector3(U(14.5f + i * 2f), DeckY, z0 + 4f), new Vector3(U(14.5f + i * 2f), DeckY, z1 - 4f), i * 0.5f);
        return footprint;
    }

    private static Material[] Materials() => _materials ?? (_materials = MeshKit.Paints("Terminal", Colours, (int)Mat.Glass));
}
