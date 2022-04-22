using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.XPath;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Verse;
using HarmonyLib;

namespace XmlPatchHelper
{
    public class XmlPatchConsole : Window
    {
		private const string XPathFieldName = "xpath";
		private static Vector2 xmlScrollPosition;

		private static float offset = 10;

		private static Regex predicateMatch = new Regex("^[a-zA-Z0-9_]+\\[[a-zA-Z0-9_]+ *= *\"[a-zA-Z0-9_)]+\"\\]");
		private static XmlDocument document;
		private static Stopwatch stopwatch;

		private static StringBuilder summary;

		private static Dictionary<Type, HashSet<string>> excludedFields = new Dictionary<Type, HashSet<string>>();

		private static Dictionary<Type, List<FieldInfo>> patchTypes = new Dictionary<Type, List<FieldInfo>>();
		private static List<PatchOperation> activeOperations = new List<PatchOperation>();

		private static Type patchType;
		private static PatchOperation patchOperation;

		private static string xpath;
		private static int nodesPerMatch;
		private static int depth;
		private static int tabs;

		public XmlPatchConsole()
		{
			GenerateCombinedXmlDoc(document == null);

			closeOnAccept = false;
			stopwatch = new Stopwatch();
			summary = new StringBuilder();

			closeOnClickedOutside = true;
			doCloseX = true;
		}

		public override Vector2 InitialSize => new Vector2(UI.screenWidth / 1.25f, UI.screenHeight / 1.25f);

		public bool GeneratingXmlDoc { get; private set; } = false;

		public static void ExcludeField(Type declaringType, string name, bool logNotFound = true)
		{
			FieldInfo field = AccessTools.Field(declaringType, name);
			if (field == null)
			{
				if (logNotFound)
				{
					Log.Error($"Unable to find {declaringType}.{name} field for excluding.");
				}
				return;
			}
			if (!excludedFields.ContainsKey(declaringType))
			{
				excludedFields.Add(declaringType, new HashSet<string>());
			}
			excludedFields[declaringType].Add(name);
		}

		public static bool Excluded(FieldInfo field)
		{
			if (excludedFields.TryGetValue(field.DeclaringType, out HashSet<string> names))
			{
				return names.Contains(field.Name);
			}
			return false;
		}

		public override void DoWindowContents(Rect inRect)
		{
			if (GeneratingXmlDoc)
			{
				return;
			}

			var font = Text.Font;
			Text.Font = GameFont.Small;

			Rect leftWindow = new Rect(0, 0, inRect.width / 2.25f - offset, inRect.height);
			DoXmlArea(leftWindow);
			Rect rightWindow = new Rect(leftWindow.width + offset * 2, leftWindow.y, inRect.width - leftWindow.width - offset * 2, inRect.height);
			DoPatchArea(rightWindow);

			Text.Font = font;
		}

		private void DoXmlArea(Rect rect)
		{
			Rect buttonRect = new Rect(0, 0, rect.width / 4 - 1, Dialog_ColorPicker.ButtonHeight);
			if (Widgets.ButtonText(buttonRect, "RunXPath".Translate()))
			{
				UpdateSelect();
			}
			buttonRect.x += buttonRect.width + 1;
			if (Widgets.ButtonText(buttonRect, "XPathProfile".Translate()))
			{
				ProfileSelect(10000);
			}
			buttonRect.x += buttonRect.width + 1;
			if (Widgets.ButtonText(buttonRect, "RegenerateXmlDoc".Translate()))
			{
				GenerateCombinedXmlDoc(false);
				return;
			}
			buttonRect.x += buttonRect.width + 1;
			if (Widgets.ButtonText(buttonRect, "DownloadMatches".Translate()))
			{
				string filePath = Path.Combine(Application.persistentDataPath, "XmlPatchHelper_XPathResult.xml");
				try
				{
					XmlNodeList nodeList = document.SelectNodes(xpath);
					XmlDocument matchDoc = new XmlDocument();
					if (nodeList.Count > 1)
					{
						matchDoc.AppendChild(matchDoc.CreateElement("XPathMatches"));
					}
					foreach (XmlNode node in nodeList)
					{
						matchDoc.DocumentElement.AppendChild(matchDoc.ImportNode(node, true));
					}
					matchDoc.Save(filePath);
					Application.OpenURL(filePath);
				}
				catch (Exception ex)
				{
					Log.Error($"Unable to download XmlDocument to {filePath} Exception = {ex}");
				}
			}
			rect.y = buttonRect.height + 2;
			rect.height -= buttonRect.height + 2;
			Widgets.DrawMenuSection(rect);
			rect = rect.ContractedBy(5);
			Widgets.LabelScrollable(rect, summary.ToString(), ref xmlScrollPosition);
		}

		private void DoPatchArea(Rect rect)
		{
			Rect fieldRect = rect;
			fieldRect.y = Dialog_ColorPicker.ButtonHeight + 2;
			fieldRect.height = 24;
			Widgets.Label(fieldRect, "PatchOperationType".Translate());
			fieldRect.x += Text.CalcSize("PatchOperationType".Translate()).x + 5;
			fieldRect.width = rect.width / 2;
			Widgets.Dropdown(fieldRect, patchType, (Type type) => type, PatchTypeSelection_GenerateMenu, patchType.Name);
			fieldRect.x = rect.x;
			fieldRect.y += 26;
			fieldRect.width = rect.width;
			if (patchType != null && patchOperation != null)
			{
				foreach (FieldInfo fieldInfo in patchTypes[patchType])
				{
					InputField(ref fieldRect, fieldInfo.Name, fieldInfo, (object value) =>
					{
						if (fieldInfo.Name == XPathFieldName)
						{
							xpath = (string)value;
							UpdateSelect();
						}
					});
					fieldRect.y += fieldRect.height + 5;
				}
			}
		}

		/// <summary>
		/// Update selection given <see cref="xpath"/>
		/// </summary>
		private void UpdateSelect()
		{
			summary.Clear();
			try
			{
				XmlNodeList nodeList; //Create object outside timer

				stopwatch.Restart();
				nodeList = document.SelectNodes(xpath);
				stopwatch.Stop();

				BuildXmlSummary(nodeList);
				
				string disclaimer = nodeList.Count >= 5 ? "(Only showing first 5 matches)" : string.Empty;
				summary.PrependLine();
				summary.PrependLine(XmlText.Comment($"Execution time: {string.Format("{0:0.##}", stopwatch.ElapsedTicks)} ticks ({string.Format("{0:0.##}", stopwatch.ElapsedMilliseconds)}ms)"));
				summary.PrependLine(XmlText.Comment($"Summary: Found {nodeList.Count} results. {disclaimer}"));

				if (nodeList.Count == 0)
				{
					//Attempt auto-correct
					try
					{
						string[] xpathTree = Regex.Split(xpath, @"(\/{1,2})");
						string lastSelect = xpathTree.LastOrDefault();
						if (predicateMatch.IsMatch(lastSelect))
						{
							string[] lastSelectPieceMeal = Regex.Split(lastSelect, @"(?=[\[])");
							lastSelectPieceMeal[0] = "*"; //wild card match
							xpathTree[xpathTree.Length - 1] = string.Join("", lastSelectPieceMeal);
							string correctedXPath = string.Join("", xpathTree);
							XmlNodeList correctionAttempt = document.SelectNodes(correctedXPath);
							if (correctionAttempt.Count > 0)
							{
								summary.AppendLine();
								summary.AppendLine("Your search was close. Did you mean one of the following?");
								foreach (XmlNode node in correctionAttempt)
								{
									lastSelectPieceMeal[0] = node.Name; //Substitute wildcard match
									xpathTree[xpathTree.Length - 1] = string.Join("", lastSelectPieceMeal);
									correctedXPath = string.Join("", xpathTree);
									summary.AppendLine(correctedXPath.Colorize(ColorLibrary.Grey));
								}
							}
						}
					}
					catch
					{
					}
				}
			}
			catch (XPathException)
			{
				summary.Clear();
				summary.Append("Invalid xpath. Exception caught".Colorize(ColorLibrary.LogError));
			}
			summary.AppendLine();
		}

		private double ProfileSelect(int sample, int exitPointIfSlow = 100, double exitIfAboveAverageTicks = 100000)
		{
			summary.Clear();
			try
			{
				if (sample > 0)
				{
					summary.AppendLine($"Matching: {xpath}");
					summary.AppendLine($"Sample Size: {sample}");
					List<long> results = new List<long>();
					List<long> ms = new List<long>();
					XmlNodeList nodeList = null;
					for (int i = 0; i < sample; i++)
					{
						stopwatch.Restart();
						nodeList = document.SelectNodes(xpath);
						stopwatch.Stop();
						results.Add(stopwatch.ElapsedTicks);
						ms.Add(stopwatch.ElapsedMilliseconds);
						//Only check for exit point if given enough sample points to determine if operation is too slow
						if (i < exitPointIfSlow && i > exitPointIfSlow / 2)
						{
							if (results.Average() >= exitIfAboveAverageTicks)
							{
								summary.AppendLine($"Exited operation after {i} iterations. Operation will take take too long to calculate, which may cause the application to hang.");
								break;
							}
						}
					}
					File.WriteAllLines(Path.Combine(Application.persistentDataPath, "ResultsTicks.txt"), results.Select(r => r.ToString()));
					Application.OpenURL(Path.Combine(Application.persistentDataPath, "ResultsTicks.txt"));
					double average = results.Average();
					double averageMS = ms.Average();
					summary.AppendLine($"Matched {nodeList.Count} nodes");
					summary.AppendLine($"Average: {string.Format("{0:0.##}", average)} ticks ({string.Format("{0:0.##}", averageMS)}ms)");
					return average;
				}
			}
			catch (XPathException)
			{
				summary.Clear();
				summary.Append("Invalid xpath. Exception caught".Colorize(ColorLibrary.LogError));
			}
			return 0;
		}

		private void BuildXmlSummary(XmlNodeList nodeList)
		{
			int i = 0;
			foreach (XmlNode node in nodeList)
			{
				depth = 0;
				tabs = 0;
				nodesPerMatch = 0;
				AppendXmlRecursive(node);
				if (i >= 5)
				{
					break;
				}
				i++;
				summary.AppendLine();
			}
		}

		private bool AppendXmlRecursive(XmlNode node, int depth = 5, int nodesPerMatch = 10)
		{
			XmlAttributeCollection attributes = node.Attributes;
			if (node.IsEmpty())
			{
				summary.AppendLine(XmlText.OpenSelfClosingBracket(node.Name, tabs, attributes));
				return true;
			}
			bool truncate = false;
			if (node.IsTextOnly())
			{
				summary.AppendLine(XmlText.Text(node.InnerText, tabs));
				return true;
			}
			summary.Append(XmlText.OpenBracket(node.Name, tabs, attributes));
			if (node.IsTextElement())
			{
				summary.Append(XmlText.Text(node.InnerText.TruncateString(25)));
				summary.AppendLine(XmlText.CloseBracket(node.Name));
				return true;
			}
			else if (node.HasChildNodes)
			{
				summary.AppendLine();
				tabs++;
				if (XmlPatchConsole.depth > depth)
				{
					summary.AppendLine(XmlText.Comment("...", tabs));
					tabs--;
					return false;
				}
				XmlPatchConsole.depth++;
				foreach (XmlNode childNode in node)
				{
					if (XmlPatchConsole.nodesPerMatch >= nodesPerMatch)
					{
						truncate = true;
						break;
					}
					if (!AppendXmlRecursive(childNode))
					{
						break;
					}
					XmlPatchConsole.nodesPerMatch++;
				}
				if (truncate)
				{
					summary.AppendLine(XmlText.Comment("...", tabs));
				}
				tabs--;
			}
			summary.AppendLine(XmlText.CloseBracket(node.Name, tabs));
			return true;
		}

		public void GenerateCombinedXmlDoc(bool freshGen)
		{
			List<LoadableXmlAsset> xmls = new List<LoadableXmlAsset>();
			var assetLookup = new Dictionary<XmlNode, LoadableXmlAsset>();
			LongEventHandler.QueueLongEvent(delegate ()
			{
				GeneratingXmlDoc = true;
				foreach (ModContentPack modContentPack in LoadedModManager.RunningMods)
				{
					xmls.AddRange(DirectXmlLoader.XmlAssetsInModFolder(modContentPack, "Defs/", null).ToList());
				}
				document = LoadedModManager.CombineIntoUnifiedXML(xmls, assetLookup);
				
				if (freshGen)
				{
					patchTypes = new Dictionary<Type, List<FieldInfo>>();
					activeOperations = new List<PatchOperation>();
					foreach (Type type in typeof(PatchOperationPathed).AllSubclassesNonAbstract())
					{
						patchTypes[type] = new List<FieldInfo>();
						activeOperations.Add((PatchOperationPathed)Activator.CreateInstance(type));
						foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(field => !Excluded(field))
																																		.OrderBy(field => field.FieldType == typeof(XmlContainer))
																																		.ThenByDescending(field => field.Name))
						{
							patchTypes[type].Add(field);
						}
					}
				}
				patchType ??= patchTypes.FirstOrDefault().Key;
				patchOperation ??= activeOperations.FirstOrDefault(po => po.GetType() == patchType);
				GeneratingXmlDoc = false;
			}, "FetchingXmlDefs", true, (Exception ex) =>
			{
				Log.Error($"Exception thrown while attempting to generate combiend xml doc.");
				GeneratingXmlDoc = false;
				Close(false);
			});
		}

		private static void InputField(ref Rect rect, string label, FieldInfo field, Action<object> onValueChange = null)
		{
			object value = field.GetValue(patchOperation);
			Rect inputRect = new Rect(rect);
			if (field.FieldType == typeof(string))
			{
				inputRect.height = 24;
				string leftBracket = XmlText.OpenBracket(label);
				string rightBracket = XmlText.CloseBracket(label);
				float lbrWidth = Text.CalcSize(leftBracket).x;
				float rbrWidth = Text.CalcSize(rightBracket).x;
				Widgets.LabelFit(inputRect, leftBracket);
				inputRect.y += inputRect.height;
				string newValue = TextArea(inputRect, (string)value, true, null, TextAnchor.MiddleLeft);
				if (newValue != (string)value)
				{
					value = newValue;
					onValueChange?.Invoke(value);
				}
				inputRect.y += inputRect.height;
				Widgets.LabelFit(inputRect, rightBracket);
			}
			else if (field.FieldType == typeof(int) || field.FieldType == typeof(float))
			{
				rect.height = 24;
				int intVal = (int)value;
				string leftBracket = XmlText.OpenBracket(label);
				string rightBracket = XmlText.CloseBracket(label);
				string buffer = string.Empty;
				float lbrWidth = Text.CalcSize(leftBracket).x;
				float rbrWidth = Text.CalcSize(rightBracket).x;
				float inputWidth = rect.width - lbrWidth - rbrWidth;
				Widgets.LabelFit(inputRect, leftBracket);
				inputRect.x += lbrWidth;
				Widgets.TextFieldNumeric(inputRect, ref intVal, ref buffer);
				if (!intVal.Equals(value))
				{
					value = intVal;
					onValueChange?.Invoke(value);
				}
				inputRect.x += Text.CalcSize(value.ToString()).x;
				Widgets.LabelFit(inputRect, rightBracket);
			}
			else if (field.FieldType.IsEnum)
			{
				inputRect.height = 24;
				string leftBracket = XmlText.OpenBracket(label);
				string rightBracket = XmlText.CloseBracket(label);
				float lbrWidth = Text.CalcSize(leftBracket).x;
				float rbrWidth = Text.CalcSize(rightBracket).x;
				Widgets.LabelFit(inputRect, leftBracket);
				inputRect.y += 24;
				inputRect.width = rect.width / 3;
				Widgets.Dropdown(inputRect, value.ToString(), (string name) => (int)Enum.Parse(field.FieldType, name, true), (string name) =>
				{
					List<Widgets.DropdownMenuElement<int>> elements = new List<Widgets.DropdownMenuElement<int>>();
					foreach (string enumName in Enum.GetNames(field.FieldType))
					{
						int enumValue = (int)Enum.Parse(field.FieldType, enumName);
						elements.Add(new Widgets.DropdownMenuElement<int>
						{
							option = new FloatMenuOption(enumName, delegate ()
							{
								field.SetValue(patchOperation, enumValue);
								onValueChange?.Invoke(enumValue);
							}),
							payload = enumValue
						});
					}
					return elements;
				}, value.ToString());
				inputRect.y += 24;
				Widgets.LabelFit(inputRect, rightBracket);
			}
			else if (field.FieldType == typeof(XmlContainer))
			{
				if (value == null)
				{
					value = new XmlContainer()
					{
						node = document.CreateElement("value")
					};
				}
				XmlContainer container = (XmlContainer)value;
				inputRect.height = 24;
				string leftBracket = XmlText.OpenBracket(label);
				string rightBracket = XmlText.CloseBracket(label);
				float lbrWidth = Text.CalcSize(leftBracket).x;
				float rbrWidth = Text.CalcSize(rightBracket).x;
				Widgets.LabelFit(inputRect, leftBracket);
				inputRect.y += inputRect.height;
				float boxHeight = Mathf.Max(24, Text.CalcHeight(container.node.InnerText, inputRect.width)); ;
				inputRect.height = boxHeight;
				container.node.InnerText = TextArea(inputRect, container.node.InnerText, true);
				inputRect.y += boxHeight;
				inputRect.height = 24;
				Widgets.LabelFit(inputRect, rightBracket);
				value = container;
			}
			else
			{
				inputRect.height = 24;
				Widgets.LabelFit(inputRect, $"{label} (Unsupported field type = {field.FieldType})");
			}
			field.SetValue(patchOperation, value);
			rect.height = inputRect.height;
			rect.y = inputRect.y;
		}

		private static IEnumerable<Widgets.DropdownMenuElement<Type>> PatchTypeSelection_GenerateMenu(Type type)
		{
			foreach (Type registeredType in patchTypes.Keys)
			{
				yield return new Widgets.DropdownMenuElement<Type>
				{
					option = new FloatMenuOption(registeredType.Name, delegate ()
					{
						patchType = registeredType;
						patchOperation = activeOperations.FirstOrDefault(po => po.GetType() == patchType);
					}),
					payload = registeredType
				};
			}
		}

		public static string TextArea(Rect rect, string text, bool background = false, Regex validator = null, TextAnchor anchor = TextAnchor.UpperLeft)
		{
			var anchorOld = Text.Anchor;
			if (background)
			{
				Widgets.DrawMenuSection(rect);
				rect.x += 2;
			}
			Text.Anchor = anchor;
			string input = GUI.TextArea(rect, text, Text.CurFontStyle);
			Text.Anchor = anchorOld;
			if (validator == null || validator.IsMatch(input))
			{
				return input;
			}
			return text;
		}

		public static string TextAreaXmlRender(Rect rect, string text, bool background = false, Regex validator = null)
		{
			string tmpTextForRendering = text;

			
			tmpTextForRendering = TextAreaXmlRender(rect, tmpTextForRendering, background, validator);

			text = tmpTextForRendering;

			return text;
		}

		public static string TextAreaScrollable(Rect rect, string text, ref Vector2 scrollbarPosition)
		{
			Rect rect2 = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(Text.CalcHeight(text, rect.width) + 10f, rect.height));
			Widgets.BeginScrollView(rect, ref scrollbarPosition, rect2, true);
			string result = TextArea(rect, text);
			Widgets.EndScrollView();
			return result;
		}

		public static string XmlAreaScrollable(Rect rect, XmlNode node, ref Vector2 scrollbarPosition)
		{
			string text = node.InnerText;
			Rect rect2 = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(Text.CalcHeight(text, rect.width) + 10f, rect.height));
			Widgets.BeginScrollView(rect, ref scrollbarPosition, rect2, true);
			string result = TextArea(rect, text);
			Widgets.EndScrollView();
			return result;
		}
	}
}
