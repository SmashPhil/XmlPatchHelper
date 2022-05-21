using System;
using System.Text;
using System.Linq;
using System.Xml;
using Verse;
using UnityEngine;

namespace XmlPatchHelper
{
	public static class Utilities
	{
		public static object DefaultValue(this Type type)
		{
			if (type.IsValueType)
			{
				return Activator.CreateInstance(type);
			}
			return null;
		}

		public static void Fill<T>(this T[] array, T value)
		{
			for (int i = 0; i <array.Length; i++)
			{
				array[i] = value;
			}
		}

		public static string TruncateString(this string source, int maxLength)
		{
			if (string.IsNullOrEmpty(source))
			{
				return source;
			}
			if (source.Length <= maxLength)
			{
				return source;
			}
			return source.Substring(0, maxLength) + "...";
		}

		public static void Prepend(this StringBuilder stringBuilder, string content)
		{
			stringBuilder.Insert(0, content);
		}

		public static void PrependLine(this StringBuilder stringBuilder, string content = "")
		{
			stringBuilder.Insert(0, $"{content}{Environment.NewLine}");
		}

		public static bool IsEmpty(this XmlNode node)
		{
			return node is null || (node is XmlElement element && element.IsEmpty);
		}

		public static bool IsTextOnly(this XmlNode node)
		{
			return node != null && node.Name == "#text";
		}

		public static bool IsTextElement(this XmlNode node)
		{
			return node != null && node.InnerXml.FirstOrDefault() != '<';
		}
	}
}
