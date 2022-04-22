using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using UnityEngine;

namespace XmlPatchHelper
{
	/// <summary>
	/// Resizes icon drawing so a scrub like me can use the 1024 x 1024 image I pulled off google
	/// </summary>
	public class ListableOption_WebLinkResized : ListableOption_WebLink
	{
		public static Vector2 ImageSize = new Vector2(18, 18);

		public ListableOption_WebLinkResized(string label, Texture2D image) : base(label, image)
		{
		}

		public ListableOption_WebLinkResized(string label, string url, Texture2D image) : base(label, url, image)
		{
		}

		public ListableOption_WebLinkResized(string label, Action action, Texture2D image) : base(label, action, image)
		{
		}

		public override float DrawOption(Vector2 pos, float width)
		{
			float num = width - ImageSize.x - 3f;
			float num2 = Text.CalcHeight(label, num);
			float num3 = Mathf.Max(minHeight, num2);
			Rect rect = new Rect(pos.x, pos.y, width, num3);
			GUI.color = Color.white;
			if (image != null)
			{
				Rect position = new Rect(pos.x, pos.y + num3 / 2f - ImageSize.y / 2f, ImageSize.x, ImageSize.y);
				if (Mouse.IsOver(rect))
				{
					GUI.color = Widgets.MouseoverOptionColor;
				}
				GUI.DrawTexture(position, image);
			}
			Widgets.Label(new Rect(rect.xMax - num, pos.y, num, num2), label);
			GUI.color = Color.white;
			if (Widgets.ButtonInvisible(rect, true))
			{
				action?.Invoke();
			}
			return num3;
		}
	}
}
