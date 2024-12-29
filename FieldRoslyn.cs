using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MinimalisticWPF.Generator
{
    public class FieldRoslyn
    {
        internal FieldRoslyn(IFieldSymbol fieldSymbol)
        {
            TypeName = fieldSymbol.Type.ToString();
            FieldName = fieldSymbol.Name;
            PropertyName = GetPropertyNameFromFieldName(fieldSymbol.Name);
            ThemeAttributes = GetThemeAttributesTexts(fieldSymbol);
        }

        public string TypeName { get; private set; } = string.Empty;
        public string FieldName { get; private set; } = string.Empty;
        public string PropertyName { get; private set; } = string.Empty;
        public List<string> ThemeAttributes { get; private set; } = [];

        private static string GetPropertyNameFromFieldName(string fieldName)
        {
            if (fieldName.StartsWith("_"))
            {
                return char.ToUpper(fieldName[1]) + fieldName.Substring(2);
            }
            else
            {
                return char.ToUpper(fieldName[0]) + fieldName.Substring(1);
            }
        }
        private static List<string> GetThemeAttributesTexts(IFieldSymbol fieldSymbol)
        {
            List<string> result = [];
            foreach (var attribute in fieldSymbol.GetAttributes())
            {
                if (attribute.AttributeClass == null) continue;

                if (attribute.AttributeClass.AllInterfaces.Any(i => i.Name == "IThemeAttribute"))
                {
                    var final = attribute.ApplicationSyntaxReference?.GetSyntax().ToFullString();
                    result.Add(final == null ? string.Empty : $"[{final}]");
                }
            }
            return result;
        }

        public string GenerateCode()
        {
            StringBuilder sb = new();

            foreach (var attributeText in ThemeAttributes)
            {
                sb.AppendLine($"      {attributeText}");
            }
            sb.AppendLine($"      public virtual {TypeName} {PropertyName}");
            sb.AppendLine("      {");
            sb.AppendLine($"         get => {FieldName};");
            sb.AppendLine("         set");
            sb.AppendLine("         {");
            sb.AppendLine($"            {TypeName} oldValue = {FieldName};");
            sb.AppendLine($"            if( value != {FieldName} && !{PropertyName}Interception(oldValue,value))");
            sb.AppendLine("            {");
            sb.AppendLine($"               On{PropertyName}Changing(oldValue,value);");
            sb.AppendLine($"               {FieldName} = value;");
            sb.AppendLine($"               On{PropertyName}Changed(oldValue,value);");
            sb.AppendLine($"               OnPropertyChanged(nameof({PropertyName}));");
            sb.AppendLine("            }");
            sb.AppendLine("         }");
            sb.AppendLine("      }");

            sb.AppendLine($"      private partial bool {PropertyName}Interception({TypeName} oldValue,{TypeName} newValue);");
            sb.AppendLine($"      private partial void On{PropertyName}Changing({TypeName} oldValue,{TypeName} newValue);");
            sb.AppendLine($"      private partial void On{PropertyName}Changed({TypeName} oldValue,{TypeName} newValue);");

            return sb.ToString();
        }
    }
}
