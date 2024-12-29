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
            SetterValidation = GetSetterValidateType(fieldSymbol);
        }

        public string TypeName { get; private set; } = string.Empty;
        public string FieldName { get; private set; } = string.Empty;
        public string PropertyName { get; private set; } = string.Empty;
        public List<string> ThemeAttributes { get; private set; } = [];
        public int SetterValidation { get; private set; } = 0;

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
        private static int GetSetterValidateType(IFieldSymbol fieldSymbol)
        {
            AttributeData attributeData = fieldSymbol.GetAttributes()
                .First(ad => ad.AttributeClass?.Name == "ObservableAttribute");

            if (attributeData.ConstructorArguments.Length > 0)
            {
                return (int)attributeData.ConstructorArguments[0].Value!;
            }
            return 0;
        }

        public string GenerateCode()
        {
            StringBuilder sb = new();

            foreach (var attributeText in ThemeAttributes)
            {
                sb.AppendLine($"      {attributeText}");
            }
            sb.AppendLine($"      public {TypeName} {PropertyName}");
            sb.AppendLine("      {");
            sb.AppendLine($"         get => {FieldName};");
            sb.AppendLine("         set");
            sb.AppendLine("         {");
            sb.AppendLine($"            {TypeName} oldValue = {FieldName};");
            switch (SetterValidation)
            {
                case 1:
                    sb.AppendLine($"            if(value != {FieldName})");
                    sb.AppendLine("            {");
                    break;
                case 2:
                    sb.AppendLine($"            if(!{PropertyName}Intercepting(oldValue,value))");
                    sb.AppendLine("            {");
                    break;
            }
            sb.AppendLine($"               On{PropertyName}Changing(oldValue,value);");
            sb.AppendLine($"               {FieldName} = value;");
            sb.AppendLine($"               On{PropertyName}Changed(oldValue,value);");
            sb.AppendLine($"               OnPropertyChanged(nameof({PropertyName}));");
            if (SetterValidation == 1 || SetterValidation == 2)
            {
                sb.AppendLine("            }");
            }
            sb.AppendLine("         }");
            sb.AppendLine("      }");

            if (SetterValidation == 2)
            {
                sb.AppendLine($"      private partial bool {PropertyName}Intercepting({TypeName} oldValue,{TypeName} newValue);");
            }
            sb.AppendLine($"      partial void On{PropertyName}Changing({TypeName} oldValue,{TypeName} newValue);");
            sb.AppendLine($"      partial void On{PropertyName}Changed({TypeName} oldValue,{TypeName} newValue);");

            return sb.ToString();
        }
    }
}
