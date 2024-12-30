using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MinimalisticWPF.Generator
{
    public class ClassRoslyn
    {
        internal ClassRoslyn(ClassDeclarationSyntax classDeclarationSyntax, INamedTypeSymbol namedTypeSymbol)
        {
            Syntax = classDeclarationSyntax;
            Symbol = namedTypeSymbol;
            IsAop = AnalizeHelper.IsAopClass(classDeclarationSyntax);
            IsDynamicTheme = AnalizeHelper.IsDynamicTheme(classDeclarationSyntax);
            IsViewModel = AnalizeHelper.IsVMFieldExist(namedTypeSymbol, out var vmfields);
            if (vmfields != null)
            {
                FieldRoslyns = vmfields.Select(field => new FieldRoslyn(field));
            }
        }

        public ClassDeclarationSyntax Syntax { get; private set; }
        public INamedTypeSymbol Symbol { get; private set; }
        public bool IsAop { get; private set; } = false;
        public bool IsDynamicTheme { get; private set; } = false;
        public bool IsViewModel { get; private set; } = false;
        public IEnumerable<FieldRoslyn> FieldRoslyns { get; private set; } = [];

        public string GenerateUsing()
        {
            StringBuilder sourceBuilder = new();
            sourceBuilder.AppendLine("#nullable enable");
            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine("using System;");
            sourceBuilder.AppendLine("using System.ComponentModel;");
            sourceBuilder.AppendLine("using MinimalisticWPF;");
            if (IsDynamicTheme)
            {
                sourceBuilder.AppendLine("using MinimalisticWPF.StructuralDesign.Theme;");
            }
            if (IsAop)
            {
                sourceBuilder.AppendLine("using MinimalisticWPF.AopInterfaces;");
            }
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
        public string GeneratePartialClass()
        {
            StringBuilder sourceBuilder = new();
            string share = $"{Syntax.Modifiers} class {Syntax.Identifier.Text}";
            var list = new List<string>();
            if (IsAop)
            {
                list.Add(AnalizeHelper.GetInterfaceName(Syntax));
            }
            if (IsViewModel)
            {
                list.Add("INotifyPropertyChanged");
            }
            if (IsDynamicTheme)
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
            return sourceBuilder.ToString();
        }
        public string GenerateConstructor()
        {
            var methods = Symbol.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => m.GetAttributes().Any(att => att.AttributeClass?.Name == "ConstructorAttribute"))
                .ToList();

            StringBuilder builder = new();
            var strAop = $"IAop{Symbol.Name}In{Symbol.ContainingNamespace.ToString().Replace('.', '_')}";
            if (IsAop)
            {
                builder.AppendLine($$"""
                                           public {{strAop}} Proxy { get; private set; }
                                     """);
                builder.AppendLine();
            }

            builder.AppendLine($"      public {Symbol.Name} ()");
            builder.AppendLine("      {");
            if (IsAop)
            {
                builder.AppendLine($"         Proxy = this.CreateProxy<{strAop}>();");
            }
            if (IsDynamicTheme)
            {
                builder.AppendLine($"         this.ApplyGlobalTheme();");
            }
            foreach (var method in methods.Where(m => !m.Parameters.Any()))
            {
                builder.AppendLine($"         {method.Name}();");
            }
            builder.AppendLine("      }");

            var groupedMethods = methods.Where(m => m.Parameters.Any()).GroupBy(m =>
                string.Join(",", m.Parameters.Select(p => p.Type.ToDisplayString())));

            foreach (var group in groupedMethods)
            {
                var parameters = group.Key.Split(',');
                var parameterList = string.Join(", ", group.First().Parameters.Select(p => $"{p.Type.ToDisplayString()} {p.Name}"));
                var callParameters = string.Join(", ", group.First().Parameters.Select(p => p.Name));

                builder.AppendLine();
                builder.AppendLine($"      public {Symbol.Name} ({parameterList})");
                builder.AppendLine("      {");
                if (IsAop)
                {
                    builder.AppendLine($"         Proxy = this.CreateProxy<{strAop}>();");
                }
                if (IsDynamicTheme)
                {
                    builder.AppendLine($"         this.ApplyGlobalTheme();");
                }
                foreach (var method in group)
                {
                    builder.AppendLine($"         {method.Name}({callParameters});");
                }
                builder.AppendLine("      }");
            }

            return builder.ToString();
        }
        public string GenerateIPC()
        {
            if (!IsViewModel) return string.Empty;
            StringBuilder sourceBuilder = new();
            string source = $$"""
                                    public event PropertyChangedEventHandler? PropertyChanged;
                                    public void OnPropertyChanged(string propertyName)
                                    {
                                       PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                                    }
                              """;
            sourceBuilder.AppendLine(source);
            return sourceBuilder.ToString();
        }
        public string GenerateITA()
        {
            if (!IsDynamicTheme) return string.Empty;
            StringBuilder sourceBuilder = new();
            sourceBuilder.AppendLine("      public bool IsThemeChanging { get; set; } = false;");
            sourceBuilder.AppendLine("      public Type? CurrentTheme { get; set; } = null;");
            sourceBuilder.AppendLine("      public partial void OnThemeChanging(Type? oldTheme, Type newTheme);");
            sourceBuilder.AppendLine("      public partial void OnThemeChanged(Type? oldTheme, Type newTheme);");
            return sourceBuilder.ToString();
        }
        public string GenerateHoverControl()
        {
            var hoverables = FieldRoslyns.Where(fr => fr.CanHover);
            StringBuilder sourceBuilder = new();

            foreach (var fieldRoslyn in hoverables)
            {
                if (IsDynamicTheme && fieldRoslyn.ThemeAttributes.Count > 0)
                {
                    foreach (var themeText in fieldRoslyn.ThemeAttributes.Select(t => t.Split('(')[0]))
                    {
                        sourceBuilder.AppendLine($"      private {fieldRoslyn.TypeName} _{themeText}Hovered{fieldRoslyn.PropertyName};");
                        sourceBuilder.AppendLine($"      public {fieldRoslyn.TypeName} {themeText}Hovered{fieldRoslyn.PropertyName}");
                        sourceBuilder.AppendLine("      {");
                        sourceBuilder.AppendLine($"         get => _{themeText}Hovered{fieldRoslyn.PropertyName};");
                        sourceBuilder.AppendLine("         set");
                        sourceBuilder.AppendLine("         {");
                        sourceBuilder.AppendLine($"            var oldValue = _{themeText}Hovered{fieldRoslyn.PropertyName};");
                        sourceBuilder.AppendLine($"            On{themeText}Hovered{fieldRoslyn.PropertyName}Changing(oldValue,value);");
                        sourceBuilder.AppendLine($"            _{themeText}Hovered{fieldRoslyn.PropertyName} = value;");
                        sourceBuilder.AppendLine($"            On{themeText}Hovered{fieldRoslyn.PropertyName}Changed(oldValue,value);");
                        sourceBuilder.AppendLine("         }");
                        sourceBuilder.AppendLine("      }");
                        sourceBuilder.AppendLine($"      partial void On{themeText}Hovered{fieldRoslyn.PropertyName}Changing({fieldRoslyn.TypeName} oldValue,{fieldRoslyn.TypeName} newValue);");
                        sourceBuilder.AppendLine($"      partial void On{themeText}Hovered{fieldRoslyn.PropertyName}Changed({fieldRoslyn.TypeName} oldValue,{fieldRoslyn.TypeName} newValue);");
                        sourceBuilder.AppendLine();

                        sourceBuilder.AppendLine($"      private {fieldRoslyn.TypeName} _{themeText}NoHovered{fieldRoslyn.PropertyName};");
                        sourceBuilder.AppendLine($"      public {fieldRoslyn.TypeName} {themeText}NoHovered{fieldRoslyn.PropertyName}");
                        sourceBuilder.AppendLine("      {");
                        sourceBuilder.AppendLine($"         get => _{themeText}NoHovered{fieldRoslyn.PropertyName};");
                        sourceBuilder.AppendLine("         set");
                        sourceBuilder.AppendLine("         {");
                        sourceBuilder.AppendLine($"            var oldValue = _{themeText}NoHovered{fieldRoslyn.PropertyName};");
                        sourceBuilder.AppendLine($"            On{themeText}NoHovered{fieldRoslyn.PropertyName}Changing(oldValue,value);");
                        sourceBuilder.AppendLine($"            _{themeText}NoHovered{fieldRoslyn.PropertyName} = value;");
                        sourceBuilder.AppendLine($"            On{themeText}NoHovered{fieldRoslyn.PropertyName}Changed(oldValue,value);");
                        sourceBuilder.AppendLine("         }");
                        sourceBuilder.AppendLine("      }");
                        sourceBuilder.AppendLine($"      partial void On{themeText}NoHovered{fieldRoslyn.PropertyName}Changing({fieldRoslyn.TypeName} oldValue,{fieldRoslyn.TypeName} newValue);");
                        sourceBuilder.AppendLine($"      partial void On{themeText}NoHovered{fieldRoslyn.PropertyName}Changed({fieldRoslyn.TypeName} oldValue,{fieldRoslyn.TypeName} newValue);");
                        sourceBuilder.AppendLine();
                    }
                }
                else
                {
                    sourceBuilder.AppendLine($"      private {fieldRoslyn.TypeName} _Hovered{fieldRoslyn.PropertyName};");
                    sourceBuilder.AppendLine($"      public {fieldRoslyn.TypeName} Hovered{fieldRoslyn.PropertyName}");
                    sourceBuilder.AppendLine("      {");
                    sourceBuilder.AppendLine($"         get => _Hovered{fieldRoslyn.PropertyName};");
                    sourceBuilder.AppendLine("         set");
                    sourceBuilder.AppendLine("         {");
                    sourceBuilder.AppendLine($"            var oldValue = _Hovered{fieldRoslyn.PropertyName};");
                    sourceBuilder.AppendLine($"            OnHovered{fieldRoslyn.PropertyName}Changing(oldValue,value);");
                    sourceBuilder.AppendLine($"            _Hovered{fieldRoslyn.PropertyName} = value;");
                    sourceBuilder.AppendLine($"            OnHovered{fieldRoslyn.PropertyName}Changed(oldValue,value);");
                    sourceBuilder.AppendLine("         }");
                    sourceBuilder.AppendLine("      }");
                    sourceBuilder.AppendLine($"      partial void OnHovered{fieldRoslyn.PropertyName}Changing({fieldRoslyn.TypeName} oldValue,{fieldRoslyn.TypeName} newValue);");
                    sourceBuilder.AppendLine($"      partial void OnHovered{fieldRoslyn.PropertyName}Changed({fieldRoslyn.TypeName} oldValue,{fieldRoslyn.TypeName} newValue);");
                    sourceBuilder.AppendLine();

                    sourceBuilder.AppendLine($"      private {fieldRoslyn.TypeName} _NoHovered{fieldRoslyn.PropertyName};");
                    sourceBuilder.AppendLine($"      public {fieldRoslyn.TypeName} NoHovered{fieldRoslyn.PropertyName}");
                    sourceBuilder.AppendLine("      {");
                    sourceBuilder.AppendLine($"         get => _NoHovered{fieldRoslyn.PropertyName};");
                    sourceBuilder.AppendLine("         set");
                    sourceBuilder.AppendLine("         {");
                    sourceBuilder.AppendLine($"            var oldValue = _NoHovered{fieldRoslyn.PropertyName};");
                    sourceBuilder.AppendLine($"            OnNoHovered{fieldRoslyn.PropertyName}Changing(oldValue,value);");
                    sourceBuilder.AppendLine($"            _NoHovered{fieldRoslyn.PropertyName} = value;");
                    sourceBuilder.AppendLine($"            OnNoHovered{fieldRoslyn.PropertyName}Changed(oldValue,value);");
                    sourceBuilder.AppendLine("         }");
                    sourceBuilder.AppendLine("      }");
                    sourceBuilder.AppendLine($"      partial void OnNoHovered{fieldRoslyn.PropertyName}Changing({fieldRoslyn.TypeName} oldValue,{fieldRoslyn.TypeName} newValue);");
                    sourceBuilder.AppendLine($"      partial void OnNoHovered{fieldRoslyn.PropertyName}Changed({fieldRoslyn.TypeName} oldValue,{fieldRoslyn.TypeName} newValue);");
                    sourceBuilder.AppendLine();
                }
            }

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
