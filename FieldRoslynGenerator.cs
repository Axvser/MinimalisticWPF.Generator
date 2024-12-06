using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MinimalisticWPF.Generator
{
    public class FieldRoslynGenerator
    {
        internal FieldRoslynGenerator(IEnumerable<(IFieldSymbol, IEnumerable<IMethodSymbol>)> values)
        {
            VMPropertyGenerations = values.Select(v => GenerateVMProperty(v));
        }

        /// <summary>
        /// VM属性生成结果
        /// </summary>
        public IEnumerable<string> VMPropertyGenerations { get; private set; }

        private string GenerateVMProperty((IFieldSymbol, IEnumerable<IMethodSymbol>) value)
        {
            StringBuilder sb = new StringBuilder();

            var typeName = value.Item1.Type;
            var fieldName = value.Item1.Name;
            var propertyName = char.ToUpper(fieldName[1]) + fieldName.Substring(2);
            sb.AppendLine($"      public {typeName} {propertyName}");
            sb.AppendLine("      {");
            sb.AppendLine($"         get => {fieldName};");
            sb.AppendLine("         set");
            sb.AppendLine("         {");
            sb.AppendLine($"            if( value != {fieldName})");
            sb.AppendLine("            {");
            if (value.Item2.Count() > 0)
            {
                sb.AppendLine($"               var oldValue = {fieldName};");
                sb.AppendLine($"               var eveArgs = new WatcherEventArgs(oldValue,value);");
            }
            sb.AppendLine($"               {fieldName} = value ;");
            foreach (var methodSymbol in value.Item2)
            {
                sb.AppendLine($"                {methodSymbol.Name}(eveArgs) ;");
            }
            sb.AppendLine($"                OnPropertyChanged(nameof({propertyName})) ;");
            sb.AppendLine("            }");
            sb.AppendLine("         }");
            sb.AppendLine("      }");

            return sb.ToString();
        }
    }
}
