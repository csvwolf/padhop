using System;
using System.Globalization;
using System.IO;
using System.Xml;

internal static partial class L {
 internal static readonly string PreferencePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PadHop","ui-language.txt");
 internal static readonly bool IsEnglish=ResolveLanguage()=="en";
 internal static string ResolveLanguage(){
  foreach(string arg in Environment.GetCommandLineArgs())if(arg=="--language=en")return "en";else if(arg=="--language=zh-CN")return "zh-CN";
  try{string saved=File.ReadAllText(PreferencePath).Trim();if(saved=="en" || saved=="zh-CN")return saved;}catch{}
  if(!File.Exists(PreferencePath))foreach(string arg in Environment.GetCommandLineArgs())if(arg=="--initial-language=en")return "en";else if(arg=="--initial-language=zhcn")return "zh-CN";
  return CultureInfo.CurrentUICulture.Name.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?"zh-CN":"en";
 }
 internal static string T(string text){string value;if(!IsEnglish || text==null)return text;if(English.TryGetValue(text,out value))return value;if(text.StartsWith("实验版 · ",StringComparison.Ordinal))return "Experimental · "+text.Substring(6);return text;}
 internal static string Xaml(string text){
  if(!IsEnglish)return text;
  var doc=new XmlDocument{XmlResolver=null};doc.LoadXml(text);
  foreach(XmlElement element in doc.SelectNodes("//*"))foreach(XmlAttribute attribute in element.Attributes)if(attribute.Name=="Text" || attribute.Name=="Content" || attribute.Name=="Header" || attribute.Name=="Title" || attribute.Name=="ToolTip")attribute.Value=T(attribute.Value);
  return doc.OuterXml;
 }
}
