using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MinimalisticWPF.Generator
{
    public class ClassRoslyn
    {
        internal ClassRoslyn(ClassDeclarationSyntax classDeclarationSyntax, INamedTypeSymbol namedTypeSymbol)
        {
            Syntax = classDeclarationSyntax;
            Symbol = namedTypeSymbol;
            IsDynamicTheme = namedTypeSymbol
                .GetMembers()
                .Any(member => member.GetAttributes()
                .Any(att => att.AttributeClass?.AllInterfaces.Any(i => i.Name == "IThemeAttribute") ?? false));
            IsThemeAttributeExsist = namedTypeSymbol.GetAttributes()
                .Any(att => att.AttributeClass?.Name == "DynamicThemeAttribute");
            IsAop = AnalizeHelper.IsAopClass(classDeclarationSyntax);
            IsViewModel = IsObservableFieldExist(namedTypeSymbol, out var vmfields);
            if (vmfields != null)
            {
                FieldRoslyns = vmfields.Select(field => new FieldRoslyn(field));
            }
            ReadContextConfigParams(namedTypeSymbol);
        }

        public ClassDeclarationSyntax Syntax { get; private set; }
        public INamedTypeSymbol Symbol { get; private set; }
        public bool IsAop { get; private set; } = false;
        public bool IsDynamicTheme { get; private set; } = false;
        public bool IsViewModel { get; private set; } = false;
        public bool IsThemeAttributeExsist { get; private set; } = false;
        public IEnumerable<FieldRoslyn> FieldRoslyns { get; private set; } = [];

        public bool IsContextConfig { get; private set; } = false;
        public string ViewModelTypeName { get; private set; } = string.Empty;

        private static bool FindControlBase(INamedTypeSymbol typeSymbol)
        {
            var localBaseType = typeSymbol.BaseType;
            if (localBaseType != null)
            {
                if (typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "System.Windows.Controls.Control")
                {
                    return true;
                }
                else
                {
                    if (localBaseType.BaseType != null)
                    {
                        return FindControlBase(localBaseType.BaseType);
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            else
            {
                return false;
            }
        }
        private static bool IsObservableFieldExist(INamedTypeSymbol classSymbol, out IEnumerable<IFieldSymbol> fieldSymbols)
        {
            fieldSymbols = classSymbol.GetMembers()
                    .OfType<IFieldSymbol>()
                    .Where(field => field.GetAttributes().Any(attr => attr.AttributeClass?.Name == "ObservableAttribute"));
            return fieldSymbols.Any();
        }
        public static HashSet<string> GetReferencedNamespaces(INamedTypeSymbol namedTypeSymbol)
        {
            HashSet<string> namespaces = [];

            var syntaxRef = namedTypeSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef == null)
            {
                return namespaces;
            }
            var classDecl = syntaxRef.GetSyntax() as ClassDeclarationSyntax;
            if (classDecl == null)
            {
                return namespaces;
            }
            var compilationUnit = classDecl.FirstAncestorOrSelf<CompilationUnitSyntax>();
            if (compilationUnit == null)
            {
                return namespaces;
            }

            foreach (var usingDirective in compilationUnit.Usings)
            {
                if (usingDirective != null)
                {
                    if (usingDirective.Name != null)
                    {
                        namespaces.Add($"using {usingDirective.Name};");
                    }
                }
            }

            return namespaces;
        }
        private static string GetComment(string title, string[] messages)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine($"      /* {title}");

            foreach (var message in messages)
            {
                sb.AppendLine("       * " + message);
            }

            sb.AppendLine("      */");

            return sb.ToString();
        }
        private void ReadContextConfigParams(INamedTypeSymbol fieldSymbol)
        {
            var attributeData = fieldSymbol.GetAttributes()
                .FirstOrDefault(ad => ad.AttributeClass?.Name == "DataContextConfigAttribute");
            if (attributeData == null)
            {
                IsContextConfig = false;
                return;
            }
            IsContextConfig = true;
            ViewModelTypeName = (string)attributeData.ConstructorArguments[0].Value!;
        }

        public string GenerateUsing(HashSet<string> others)
        {
            StringBuilder sourceBuilder = new();
            sourceBuilder.AppendLine("#nullable enable");
            sourceBuilder.AppendLine();
            var hashUsings = GetReferencedNamespaces(Symbol);
            hashUsings.Add("using MinimalisticWPF;");
            hashUsings.Add("using MinimalisticWPF.Animator;");
            if (IsViewModel)
            {
                hashUsings.Add("using System.ComponentModel;");
            }
            if (IsDynamicTheme)
            {
                hashUsings.Add("using MinimalisticWPF.StructuralDesign.Theme;");
            }
            if (IsAop)
            {
                hashUsings.Add("using MinimalisticWPF.AopInterfaces;");
            }
            foreach (var name in others)
            {
                hashUsings.Add(name);
            }
            foreach (var use in hashUsings)
            {
                sourceBuilder.AppendLine(use);
            }
            sourceBuilder.AppendLine();
            return sourceBuilder.ToString();
        }
        public string GenerateUsing()
        {
            StringBuilder sourceBuilder = new();
            sourceBuilder.AppendLine("#nullable enable");
            sourceBuilder.AppendLine();
            var hashUsings = GetReferencedNamespaces(Symbol);
            hashUsings.Add("using MinimalisticWPF;");
            hashUsings.Add("using MinimalisticWPF.Animator;");
            if (IsViewModel)
            {
                hashUsings.Add("using System.ComponentModel;");
            }
            if (IsDynamicTheme)
            {
                hashUsings.Add("using MinimalisticWPF.StructuralDesign.Theme;");
            }
            if (IsAop)
            {
                hashUsings.Add("using MinimalisticWPF.AopInterfaces;");
            }
            foreach (var use in hashUsings)
            {
                sourceBuilder.AppendLine(use);
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

            if (IsContextConfig)
            {
                var source = $$"""
                              {{share}}
                              {
                           """;
                if (IsDynamicTheme && !IsThemeAttributeExsist)
                {
                    sourceBuilder.AppendLine("   [DynamicTheme]");
                }
                sourceBuilder.AppendLine(source);
            }
            else
            {
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
                    if (IsDynamicTheme && !IsThemeAttributeExsist)
                    {
                        sourceBuilder.AppendLine("   [DynamicTheme]");
                    }
                    sourceBuilder.AppendLine(source);
                }
                else
                {
                    var source = $$"""
                              {{share}}
                              {
                           """;
                    if (IsDynamicTheme && !IsThemeAttributeExsist)
                    {
                        sourceBuilder.AppendLine("   [DynamicTheme]");
                    }
                    sourceBuilder.AppendLine(source);
                }
            }

            return sourceBuilder.ToString();
        }
        public string GenerateConstructor()
        {
            if (IsContextConfig)
            {
                return string.Empty;
            }

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
            if (IsContextConfig)
            {
                return string.Empty;
            }
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
            if (IsContextConfig)
            {
                return string.Empty;
            }
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
            if (IsContextConfig)
            {
                return string.Empty;
            }

            var hoverables = FieldRoslyns.Where(fr => fr.CanHover).ToArray();

            if (hoverables.Length == 0) return string.Empty;

            StringBuilder sourceBuilder = new();

            sourceBuilder.Append(GetComment("HoverControl ↓↓↓ ___________________________________",
                ["TransitionBoard => describes the hover animation",
                "(No)HoveredProperties => represents the hover effect in different states",
                "Partial Methods => you can modify the animation as these Properties change"
                ]));

            sourceBuilder.AppendLine($$"""
                      private bool _isHovered = false;
                      public bool IsHovered
                      {
                         get => _isHovered;
                         set
                         {
                            if(_isHovered != value)
                            {
                               _isHovered = value;
                               if (!IsThemeChanging)
                               {
                                   this.BeginTransition(value ? HoveredTransition : NoHoveredTransition);
                               }
                            }
                         }
                      }
                """);

            sourceBuilder.AppendLine();

            sourceBuilder.AppendLine($$"""
                      private bool _isHoverChanging = false;
                      public bool IsHoverChanging
                      {
                         get => _isHoverChanging;
                         set
                         {
                            if(_isHoverChanging != value)
                            {
                               _isHoverChanging = value;
                            }
                         }
                      }
                """);

            sourceBuilder.AppendLine();

            sourceBuilder.AppendLine($$"""
                      public TransitionBoard<{{Syntax.Identifier.Text}}> HoveredTransition { get; set; } = Transition.Create<{{Syntax.Identifier.Text}}>();
                """);
            sourceBuilder.AppendLine($$"""
                      public TransitionBoard<{{Syntax.Identifier.Text}}> NoHoveredTransition { get; set; } = Transition.Create<{{Syntax.Identifier.Text}}>()
                """);
            for (var i = 0; i < hoverables.Length; i++)
            {
                if (!string.IsNullOrEmpty(hoverables[i].Initial))
                {
                    sourceBuilder.AppendLine($"         .SetProperty(x => x.{hoverables[i].PropertyName}, {hoverables[i].Initial.Replace("=", string.Empty).TrimStart()})");
                }
            }
            sourceBuilder.Append("         ;");

            sourceBuilder.AppendLine();

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
                        sourceBuilder.AppendLine($"            _{themeText}Hovered{fieldRoslyn.PropertyName} = value;");
                        sourceBuilder.AppendLine($$"""
                                        if(CurrentTheme == typeof({{themeText}}))
                                        {
                                           HoveredTransition.SetProperty(x => x.{{fieldRoslyn.PropertyName}}, value);
                                           if(!IsHoverChanging && IsHovered && !IsThemeChanging)
                                           {
                                              {{fieldRoslyn.PropertyName}} = value;
                                           }
                                        }
                            """);
                        sourceBuilder.AppendLine($"            On{themeText}Hovered{fieldRoslyn.PropertyName}Changed(oldValue,value);");
                        sourceBuilder.AppendLine("         }");
                        sourceBuilder.AppendLine("      }");
                        sourceBuilder.AppendLine($"      partial void On{themeText}Hovered{fieldRoslyn.PropertyName}Changed({fieldRoslyn.TypeName} oldValue,{fieldRoslyn.TypeName} newValue);");
                        sourceBuilder.AppendLine();

                        sourceBuilder.AppendLine($"      private {fieldRoslyn.TypeName} _{themeText}NoHovered{fieldRoslyn.PropertyName} = ({fieldRoslyn.TypeName})DynamicTheme.GetThemeValue(typeof({Syntax.Identifier.Text}),typeof({themeText}),nameof({fieldRoslyn.PropertyName}));");
                        sourceBuilder.AppendLine($"      public {fieldRoslyn.TypeName} {themeText}NoHovered{fieldRoslyn.PropertyName}");
                        sourceBuilder.AppendLine("      {");
                        sourceBuilder.AppendLine($"         get => _{themeText}NoHovered{fieldRoslyn.PropertyName};");
                        sourceBuilder.AppendLine("         set");
                        sourceBuilder.AppendLine("         {");
                        sourceBuilder.AppendLine($"            var oldValue = _{themeText}NoHovered{fieldRoslyn.PropertyName};");
                        sourceBuilder.AppendLine($"            _{themeText}NoHovered{fieldRoslyn.PropertyName} = value;");
                        sourceBuilder.AppendLine($$"""
                                        if(CurrentTheme == typeof({{themeText}}))
                                        {
                                           NoHoveredTransition.SetProperty(x => x.{{fieldRoslyn.PropertyName}}, value);
                                           if(!IsHoverChanging && !IsHovered && !IsThemeChanging)
                                           {
                                              {{fieldRoslyn.PropertyName}} = value;
                                           }
                                        }
                            """);
                        sourceBuilder.AppendLine($"            On{themeText}NoHovered{fieldRoslyn.PropertyName}Changed(oldValue,value);");
                        sourceBuilder.AppendLine("         }");
                        sourceBuilder.AppendLine("      }");
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
                    sourceBuilder.AppendLine($"            _Hovered{fieldRoslyn.PropertyName} = value;");
                    sourceBuilder.AppendLine($$"""
                                    HoveredTransition.SetProperty(x => x.{{fieldRoslyn.PropertyName}}, value);
                        """);
                    sourceBuilder.AppendLine($"            OnHovered{fieldRoslyn.PropertyName}Changed(oldValue,value);");
                    sourceBuilder.AppendLine("         }");
                    sourceBuilder.AppendLine("      }");
                    sourceBuilder.AppendLine($"      partial void OnHovered{fieldRoslyn.PropertyName}Changed({fieldRoslyn.TypeName} oldValue,{fieldRoslyn.TypeName} newValue);");
                    sourceBuilder.AppendLine();

                    sourceBuilder.AppendLine($"      private {fieldRoslyn.TypeName} _NoHovered{fieldRoslyn.PropertyName}{fieldRoslyn.Initial};");
                    sourceBuilder.AppendLine($"      public {fieldRoslyn.TypeName} NoHovered{fieldRoslyn.PropertyName}");
                    sourceBuilder.AppendLine("      {");
                    sourceBuilder.AppendLine($"         get => _NoHovered{fieldRoslyn.PropertyName};");
                    sourceBuilder.AppendLine("         set");
                    sourceBuilder.AppendLine("         {");
                    sourceBuilder.AppendLine($"            var oldValue = _NoHovered{fieldRoslyn.PropertyName};");
                    sourceBuilder.AppendLine($"            _NoHovered{fieldRoslyn.PropertyName} = value;");
                    sourceBuilder.AppendLine($$"""
                                    NoHoveredTransition.SetProperty(x => x.{{fieldRoslyn.PropertyName}}, value);
                                    if (!IsHoverChanging && !IsHovered)
                                    {
                                        {{fieldRoslyn.PropertyName}} = newValue;
                                    }
                        """);
                    sourceBuilder.AppendLine($"            OnNoHovered{fieldRoslyn.PropertyName}Changed(oldValue,value);");
                    sourceBuilder.AppendLine("         }");
                    sourceBuilder.AppendLine("      }");
                    sourceBuilder.AppendLine($"      partial void OnNoHovered{fieldRoslyn.PropertyName}Changed({fieldRoslyn.TypeName} oldValue,{fieldRoslyn.TypeName} newValue);");
                    sourceBuilder.AppendLine();
                }
            }

            sourceBuilder.AppendLine();

            //生成主题修改后的动画效果更新函数
            sourceBuilder.AppendLine("      protected virtual void UpdateTransitionBoard()");
            sourceBuilder.AppendLine("      {");
            sourceBuilder.AppendLine("          var isdark = CurrentTheme == typeof(Dark);");
            sourceBuilder.AppendLine();
            foreach (var fieldRoslyn in hoverables)
            {
                if (IsDynamicTheme && fieldRoslyn.ThemeAttributes.Count > 0)
                {
                    foreach (var themeText in fieldRoslyn.ThemeAttributes.Select(t => t.Split('(')[0]))
                    {
                        sourceBuilder.AppendLine($"          HoveredTransition.SetProperty(b => b.{fieldRoslyn.PropertyName}, {fieldRoslyn.PropertyName}_SelectThemeValue_Hovered(nameof({themeText})));");
                        sourceBuilder.AppendLine($"          NoHoveredTransition.SetProperty(b => b.{fieldRoslyn.PropertyName}, {fieldRoslyn.PropertyName}_SelectThemeValue_NoHovered(nameof({themeText})));");
                    }
                }
                sourceBuilder.AppendLine();
            }
            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine("          this.BeginTransition(IsHovered ? HoveredTransition : NoHoveredTransition);");
            sourceBuilder.AppendLine("      }");

            //生成Hovered值选择器
            foreach (var fieldRoslyn in hoverables)
            {
                if (IsDynamicTheme && fieldRoslyn.ThemeAttributes.Count > 0)
                {
                    sourceBuilder.AppendLine($"      protected {fieldRoslyn.TypeName} {fieldRoslyn.PropertyName}_SelectThemeValue_Hovered(string themeName)");
                    sourceBuilder.AppendLine("      {");
                    sourceBuilder.AppendLine($"         switch(themeName)");
                    sourceBuilder.AppendLine("         {");
                    foreach (var themeText in fieldRoslyn.ThemeAttributes.Select(t => t.Split('(')[0]))
                    {
                        sourceBuilder.AppendLine($"            case \"{themeText}\":");
                        sourceBuilder.AppendLine($"                 return {themeText}Hovered{fieldRoslyn.PropertyName};");
                    }
                    sourceBuilder.AppendLine("         }");
                    sourceBuilder.AppendLine($"         return {fieldRoslyn.PropertyName};");
                    sourceBuilder.AppendLine("      }");
                }
                sourceBuilder.AppendLine();
            }

            //生成NoHovered值选择器
            foreach (var fieldRoslyn in hoverables)
            {
                if (IsDynamicTheme && fieldRoslyn.ThemeAttributes.Count > 0)
                {
                    sourceBuilder.AppendLine($"      protected {fieldRoslyn.TypeName} {fieldRoslyn.PropertyName}_SelectThemeValue_NoHovered(string themeName)");
                    sourceBuilder.AppendLine("      {");
                    sourceBuilder.AppendLine($"         switch(themeName)");
                    sourceBuilder.AppendLine("         {");
                    foreach (var themeText in fieldRoslyn.ThemeAttributes.Select(t => t.Split('(')[0]))
                    {
                        sourceBuilder.AppendLine($"            case \"{themeText}\":");
                        sourceBuilder.AppendLine($"                 return {themeText}NoHovered{fieldRoslyn.PropertyName};");
                    }
                    sourceBuilder.AppendLine("         }");
                    sourceBuilder.AppendLine($"         return {fieldRoslyn.PropertyName};");
                    sourceBuilder.AppendLine("      }");
                }
                sourceBuilder.AppendLine();
            }

            sourceBuilder.Append(GetComment("HoverControl ↑↑↑ ___________________________________", []));

            return sourceBuilder.ToString();
        }
        public string GenerateDependencyProperties(string localTypeName, string typeNameSpace, string typeName)
        {
            var hoverables = FieldRoslyns.Where(fr => fr.CanHover).ToArray();

            var sourceBuilder = new StringBuilder();

            foreach (var fieldRoslyn in hoverables)
            {
                if (IsDynamicTheme && fieldRoslyn.ThemeAttributes.Count > 0)
                {
                    foreach (var themeText in fieldRoslyn.ThemeAttributes.Select(t => t.Split('(')[0]))
                    {
                        sourceBuilder.AppendLine($$"""
                                   public {{fieldRoslyn.TypeName}} {{themeText}}Hovered{{fieldRoslyn.PropertyName}}
                                   {
                                      get => ({{fieldRoslyn.TypeName}})((({{typeNameSpace}}.{{typeName}})DataContext).{{themeText}}Hovered{{fieldRoslyn.PropertyName}});
                                      set => SetValue({{themeText}}Hovered{{fieldRoslyn.PropertyName}}Property, value);
                                   }
                                   public static readonly DependencyProperty {{themeText}}Hovered{{fieldRoslyn.PropertyName}}Property =
                                      DependencyProperty.Register(
                                      nameof({{themeText}}Hovered{{fieldRoslyn.PropertyName}}),
                                      typeof({{fieldRoslyn.TypeName}}),
                                      typeof({{localTypeName}}),
                                      new PropertyMetadata({{fieldRoslyn.Initial.InitialTextParse()}}, On{{themeText}}Hovered{{fieldRoslyn.PropertyName}}Changed));   
                                   private static void On{{themeText}}Hovered{{fieldRoslyn.PropertyName}}Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
                                   {
                                      if (d is {{localTypeName}} control && control.DataContext is {{typeNameSpace}}.{{typeName}} viewModel)
                                      {
                                         viewModel.{{themeText}}Hovered{{fieldRoslyn.PropertyName}} = ({{fieldRoslyn.TypeName}})e.NewValue;
                                      }
                                   }
                            """);
                        sourceBuilder.AppendLine($$"""
                                   public {{fieldRoslyn.TypeName}} {{themeText}}NoHovered{{fieldRoslyn.PropertyName}}
                                   {
                                      get => ({{fieldRoslyn.TypeName}})((({{typeNameSpace}}.{{typeName}})DataContext).{{themeText}}NoHovered{{fieldRoslyn.PropertyName}});
                                      set => SetValue({{themeText}}NoHovered{{fieldRoslyn.PropertyName}}Property, value);
                                   }
                                   public static readonly DependencyProperty {{themeText}}NoHovered{{fieldRoslyn.PropertyName}}Property =
                                      DependencyProperty.Register(
                                      nameof({{themeText}}NoHovered{{fieldRoslyn.PropertyName}}),
                                      typeof({{fieldRoslyn.TypeName}}),
                                      typeof({{localTypeName}}),
                                      new PropertyMetadata({{fieldRoslyn.Initial.InitialTextParse()}}, On{{themeText}}NoHovered{{fieldRoslyn.PropertyName}}Changed));   
                                   private static void On{{themeText}}NoHovered{{fieldRoslyn.PropertyName}}Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
                                   {
                                      if (d is {{localTypeName}} control && control.DataContext is {{typeNameSpace}}.{{typeName}} viewModel)
                                      {
                                         viewModel.{{themeText}}NoHovered{{fieldRoslyn.PropertyName}} = ({{fieldRoslyn.TypeName}})e.NewValue;
                                      }
                                   }
                            """);
                    }
                }
                else
                {
                    sourceBuilder.AppendLine($$"""
                                   public {{fieldRoslyn.TypeName}} Hovered{{fieldRoslyn.PropertyName}}
                                   {
                                      get => ({{fieldRoslyn.TypeName}})((({{typeNameSpace}}.{{typeName}})DataContext).Hovered{{fieldRoslyn.PropertyName}});
                                      set => SetValue(Hovered{{fieldRoslyn.PropertyName}}Property, value);
                                   }
                                   public static readonly DependencyProperty Hovered{{fieldRoslyn.PropertyName}}Property =
                                      DependencyProperty.Register(
                                      nameof(Hovered{{fieldRoslyn.PropertyName}}),
                                      typeof({{fieldRoslyn.TypeName}}),
                                      typeof({{localTypeName}}),
                                      new PropertyMetadata({{fieldRoslyn.Initial.InitialTextParse()}}, OnHovered{{fieldRoslyn.PropertyName}}Changed));   
                                   private static void OnHovered{{fieldRoslyn.PropertyName}}Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
                                   {
                                      if (d is {{localTypeName}} control && control.DataContext is {{typeNameSpace}}.{{typeName}} viewModel)
                                      {
                                         viewModel.Hovered{{fieldRoslyn.PropertyName}} = ({{fieldRoslyn.TypeName}})e.NewValue;
                                      }
                                   }
                            """);
                    sourceBuilder.AppendLine($$"""
                                   public {{fieldRoslyn.TypeName}} NoHovered{{fieldRoslyn.PropertyName}}
                                   {
                                      get => ({{fieldRoslyn.TypeName}})((({{typeNameSpace}}.{{typeName}})DataContext).NoHovered{{fieldRoslyn.PropertyName}});
                                      set => SetValue(NoHovered{{fieldRoslyn.PropertyName}}Property, value);
                                   }
                                   public static readonly DependencyProperty NoHovered{{fieldRoslyn.PropertyName}}Property =
                                      DependencyProperty.Register(
                                      nameof(NoHovered{{fieldRoslyn.PropertyName}}),
                                      typeof({{fieldRoslyn.TypeName}}),
                                      typeof({{localTypeName}}),
                                      new PropertyMetadata({{fieldRoslyn.Initial.InitialTextParse()}}, OnNoHovered{{fieldRoslyn.PropertyName}}Changed));   
                                   private static void OnNoHovered{{fieldRoslyn.PropertyName}}Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
                                   {
                                      if (d is {{localTypeName}} control && control.DataContext is {{typeNameSpace}}.{{typeName}} viewModel)
                                      {
                                         viewModel.NoHovered{{fieldRoslyn.PropertyName}} = ({{fieldRoslyn.TypeName}})e.NewValue;
                                      }
                                   }
                            """);
                }
                sourceBuilder.AppendLine();
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
