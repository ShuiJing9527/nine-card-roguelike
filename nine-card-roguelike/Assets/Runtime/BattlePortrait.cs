using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Qiling
{
    /// <summary>Replaceable portrait placeholder drawn as UI geometry.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BattlePortrait : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); Rect rect = rectTransform.rect; Vector2 center = rect.center;
            mesh.AddVert(center, color, new Vector2(.5f, .5f));
            const int sides = 32;
            for (int i = 0; i <= sides; i++)
            {
                float radians = i * Mathf.PI * 2 / sides; Vector2 p = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
                mesh.AddVert(center + Vector2.Scale(p, rect.size * .5f), color, (p + Vector2.one) * .5f);
                if (i > 0) mesh.AddTriangle(0, i, i + 1);
            }
        }
    }
}
