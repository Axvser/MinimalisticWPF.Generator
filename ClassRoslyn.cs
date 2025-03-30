using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MinimalisticWPF.Generator
{
    internal abstract class ClassRoslyn
    {
        protected const string NAMESPACE_AOP = "global::MinimalisticWPF.AopInterfaces.";
        protected const string NAMESPACE_THEME = "global::MinimalisticWPF.StructuralDesign.Theme.";

        internal ClassRoslyn(ClassDeclarationSyntax classDeclarationSyntax, INamedTypeSymbol namedTypeSymbol, Compilation compilation)
        {
            Syntax = classDeclarationSyntax;
            Symbol = namedTypeSymbol;
            Compilation = compilation;

            IsAop = AnalizeHelper.IsAopClass(Syntax);
            IsDynamicTheme = AnalizeHelper.IsThemeClass(Symbol, out var headThemes, out var bodyThemes);
        }

        public ClassDeclarationSyntax Syntax { get; set; }
        public INamedTypeSymbol Symbol { get; set; }
        public Compilation Compilation { get; set; }

        public bool IsAop { get; set; } = false;
        public bool IsDynamicTheme { get; set; } = false;

        public string GenerateUsing()
        {
            StringBuilder sourceBuilder = new();
            if (IsAop)
            {
                sourceBuilder.AppendLine("using global::MinimalisticWPF;");
            }
            sourceBuilder.AppendLine("using global::MinimalisticWPF.TransitionSystem;");
            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine("#nullable enable");
            sourceBuilder.AppendLine();
            return sourceBuilder.ToString();
        }
        public string GenerateNamespace()
        {
            StringBuilder sourceBuilder = new();
            sourceBuilder.AppendLine($"namespace {Symbol.ContainingNamespace}");
            sourceBuilder.AppendLine("{");
            return sourceBuilder.ToString();
        }
        public string GenerateITA()
        {
            if (!IsDynamicTheme) return string.Empty;

            StringBuilder sourceBuilder = new();
            sourceBuilder.AppendLine("      public bool IsThemeChanging { get; set; } = false;");
            sourceBuilder.AppendLine("      public Type? CurrentTheme { get; set; } = null;");
            sourceBuilder.AppendLine("      public void RunThemeChanging(Type? oldTheme, Type newTheme)");
            sourceBuilder.AppendLine("      {");
            sourceBuilder.AppendLine("         OnThemeChanging(oldTheme ,newTheme);");
            sourceBuilder.AppendLine("      }");
            sourceBuilder.AppendLine("      public void RunThemeChanged(Type? oldTheme, Type newTheme)");
            sourceBuilder.AppendLine("      {");
            sourceBuilder.AppendLine("         OnThemeChanged(oldTheme ,newTheme);");
            sourceBuilder.AppendLine("      }");
            sourceBuilder.AppendLine("      partial void OnThemeChanging(Type? oldTheme, Type newTheme);");
            sourceBuilder.AppendLine("      partial void OnThemeChanged(Type? oldTheme, Type newTheme);");
            return sourceBuilder.ToString();
        }
        public string GenerateEnd()
        {
            StringBuilder sourceBuilder = new();
            sourceBuilder.AppendLine("   }");
            sourceBuilder.AppendLine("}");
            return sourceBuilder.ToString();
        }
    }
}
