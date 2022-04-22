using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Verse;
using UnityEngine;

namespace XmlPatchHelper
{
	public class Dialog_ColorPicker : Window
	{
		public const int ButtonWidth = 90;
		public const float ButtonHeight = 30f;

		public Color color = Color.white;
		public Color selectedColor = Color.white;
		public Action<Color> setColor;

		public static float hue;
		public static float saturation;
		public static float value;

		public static string hex;

		private static Regex validator = new Regex(@"^[a-fA-F0-9]{0,6}$");

		public Dialog_ColorPicker(Color color, Action<Color> setColor)
		{
			this.color = color;
			selectedColor = color;
			this.setColor = setColor;
			doCloseX = true;
			closeOnClickedOutside = true;

			hex = ColorUtility.ToHtmlStringRGB(selectedColor);
			Color.RGBToHSV(selectedColor, out hue, out saturation, out value);
		}

		public override Vector2 InitialSize => new Vector2(375, 350 + ButtonHeight);

		public static bool HexToColor(string hexColor, out Color color) => ColorUtility.TryParseHtmlString("#" + hexColor, out color);

		public override void DoWindowContents(Rect inRect)
		{
			Rect colorContainerRect = new Rect(inRect)
			{
				height = inRect.width - 25
			};
			float curHue = hue;
			float curSaturation = saturation;
			float curValue = value;
			XmlText.DrawColorPicker(colorContainerRect, ref curHue, ref curSaturation, ref curValue, SetColor);
			if (curHue != hue || curSaturation != saturation || curValue != value)
			{
				setColor(color);
			}

			Rect buttonRect = new Rect(0f, inRect.height - ButtonHeight, inRect.width, inRect.height);
			DoBottomButtons(buttonRect);
		}

		public override void Notify_ClickOutsideWindow()
		{
			base.Notify_ClickOutsideWindow();
			setColor(selectedColor);
			Close(true);
		}

		private void DoBottomButtons(Rect rect)
		{
			Rect buttonRect = rect;
			buttonRect.width = ButtonWidth;
			buttonRect.height = ButtonHeight;
			if (Widgets.ButtonText(buttonRect, "Apply".Translate()))
			{
				Close(true);
				return;
			}
			buttonRect.x += ButtonWidth;
			if (Widgets.ButtonText(buttonRect, "Cancel".Translate()))
			{
				setColor(selectedColor);
				Close(true);
				return;
			}

			buttonRect.height = 24;
			float hexWidth = Text.CalcSize("RRGGBBAA").x + 2;

			buttonRect.x = rect.width - hexWidth - Text.CalcSize("#").x - 2;
			Widgets.Label(buttonRect, "#");

			buttonRect.width = hexWidth;
			buttonRect.x = rect.width - buttonRect.width - 2;
			hex = XmlPatchConsole.TextArea(buttonRect, hex.ToUpperInvariant(), true, validator);
			if (HexToColor(hex, out Color hexColor) && hexColor.a == 1)
			{
				setColor(hexColor);
				Color.RGBToHSV(hexColor, out hue, out saturation, out value);
			}
		}

		private void SetColor(float hue, float saturation, float value)
		{
			color = new ColorInt(Color.HSVToRGB(hue, saturation, value)).ToColor;
			hex = ColorUtility.ToHtmlStringRGB(color);
		}
	}
}