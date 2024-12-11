using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MinimalisticWPF.Generator
{
    public class ConstructorRoslynGenerator
    {
        internal ConstructorRoslynGenerator(INamedTypeSymbol namedTypeSymbol, bool isAop, bool isDynTheme)
        {
            GenerateConstructor(namedTypeSymbol, isAop, isDynTheme);
        }

        public string Constructor { get; private set; } = string.Empty;

        private void GenerateConstructor(INamedTypeSymbol namedTypeSymbol, bool isAop, bool isDynTheme)
        {
            var methods = namedTypeSymbol.GetMembers().OfType<IMethodSymbol>().Where(m => m.GetAttributes().Any(att => att.AttributeClass?.Name == "VMInitializationAttribute"));
            StringBuilder builder = new StringBuilder();
            var strAop = $"IAop{namedTypeSymbol.Name}In{namedTypeSymbol.GetNameSpaceWithOutDot()}";
            if (isAop)
            {
                builder.AppendLine($$"""      
                                           public {{strAop}} Proxy { get; private set; }
                                     """);
                builder.AppendLine();
            }
            builder.AppendLine($"      public {namedTypeSymbol.Name} ()");
            builder.AppendLine("      {");
            if (isAop)
            {
                builder.AppendLine($"         Proxy = this.CreateProxy<{strAop}>();");
            }
            if (isDynTheme)
            {
                builder.AppendLine($"         this.ApplyGlobalTheme();");
            }
            foreach (var method in methods)
            {
                builder.AppendLine($"         {method.Name}();");
            }
            builder.AppendLine("      }");
            Constructor = builder.ToString();
        }
    }
}
