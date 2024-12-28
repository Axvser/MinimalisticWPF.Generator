using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MinimalisticWPF.Generator
{
    internal static class BuildHelper
    {
        internal static void GenerateUsing(this StringBuilder sourceBuilder, bool isAop, bool isDyn)
        {
            sourceBuilder.AppendLine("#nullable enable");
            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine("using System.ComponentModel;");
            sourceBuilder.AppendLine("using MinimalisticWPF;");
            if (isDyn)
            {
                sourceBuilder.AppendLine("using MinimalisticWPF.StructuralDesign.Theme;");
            }
            if (isAop)
            {
                sourceBuilder.AppendLine("using MinimalisticWPF.AopInterfaces;");
            }
            sourceBuilder.AppendLine();
        }
        internal static void GenerateNamespace(this StringBuilder sourceBuilder, INamedTypeSymbol classSymbol)
        {
            sourceBuilder.AppendLine($"namespace {classSymbol.ContainingNamespace}");
            sourceBuilder.AppendLine("{");
        }
        internal static void GenerateVMClassPartialClass(this StringBuilder sourceBuilder, ClassDeclarationSyntax cs, bool isAop, bool isVM, bool isDy)
        {
            string share = $"{cs.Modifiers} class {cs.Identifier.Text}";
            var list = new List<string>();
            if (isAop)
            {
                list.Add(AnalizeHelper.GetInterfaceName(cs));
            }
            if (isVM)
            {
                list.Add("INotifyPropertyChanged");
            }
            if (isDy)
            {
                list.Add("IThemeApplied");
            }
            if (list.Count > 0)
            {
                var result = string.Join(", ", list);
                var source = $$"""
                              {{share}} : {{result}}
                              {
                           """;
                sourceBuilder.AppendLine(source);
            }
            else
            {
                var source = $$"""
                              {{share}}
                              {
                           """;
                sourceBuilder.AppendLine(source);
            }
        }
        internal static void GenerateIPC(this StringBuilder sourceBuilder, bool isVM)
        {
            if (!isVM) return;
            string source = $$"""
                                    public event PropertyChangedEventHandler? PropertyChanged;
                                    public void OnPropertyChanged(string propertyName)
                                    {
                                       PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                                    }
                              """;
            sourceBuilder.AppendLine(source);
        }
        internal static void GenerateITA(this StringBuilder sourceBuilder, Tuple<IEnumerable<IMethodSymbol>, IEnumerable<IMethodSymbol>> tuple)
        {
            sourceBuilder.AppendLine("      public virtual bool IsThemeChanging { get; set; } = false;");
            sourceBuilder.AppendLine("      public virtual Type? NowTheme { get; set; } = null;");
            sourceBuilder.AppendLine("      public virtual void BeforeThemeChanged()");
            sourceBuilder.AppendLine("      {");
            foreach (var before in tuple.Item1)
            {
                sourceBuilder.AppendLine($"         {before.Name}();");
            }
            sourceBuilder.AppendLine("      }");
            sourceBuilder.Append(string.Empty);
            sourceBuilder.AppendLine("      public virtual void AfterThemeChanged()");
            sourceBuilder.AppendLine("      {");
            foreach (var after in tuple.Item2)
            {
                sourceBuilder.AppendLine($"         {after.Name}();");
            }
            sourceBuilder.AppendLine("      }");
        }
        internal static void GenerateEnd(this StringBuilder sourceBuilder)
        {
            sourceBuilder.AppendLine("   }");
            sourceBuilder.AppendLine("}");
        }
        internal static void AddVMClassSource(this SourceProductionContext context, INamedTypeSymbol classSymbol, ClassDeclarationSyntax classDeclaration, StringBuilder stringBuilder)
        {
            context.AddSource($"{classSymbol.ContainingNamespace.ToString().Replace('.', '_')}_{classDeclaration.Identifier.Text}_VMClass.g.cs", SourceText.From(stringBuilder.ToString(), Encoding.UTF8));
        }
    }
}
