using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MinimalisticWPF.Generator.Factory;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace MinimalisticWPF.Generator
{
    internal class ViewRoslyn : ClassRoslyn
    {
        const string NAMESPACE_CONFIG = "global::MinimalisticWPF.";
        const string FULLNAME_THEMECONFIG = "global::MinimalisticWPF.ThemeAttribute";
        const string FULLNAME_HOVERCONFIG = "global::MinimalisticWPF.HoverAttribute";
        const string NAMESPACE_DP = "global::System.Windows.";
        const string NAMESPACE_CONSTRUCTOR = "global::MinimalisticWPF.";
        const string TAG_PROXY = "_proxy";

        internal ViewRoslyn(ClassDeclarationSyntax classDeclarationSyntax, INamedTypeSymbol namedTypeSymbol, Compilation compilation) : base(classDeclarationSyntax, namedTypeSymbol, compilation)
        {
            Hovers = GetHoverAttributesTexts();
            Themes = GetThemeAttributesTexts();
            ReadContextConfigParams(namedTypeSymbol);
            IsView = IsUIElement(namedTypeSymbol) && (Hovers.Count > 0 || Themes.Count > 0 || (DataContextSyntax != null && DataContextSymbol != null));
        }

        public bool IsView { get; set; } = false;

        public ClassDeclarationSyntax? DataContextSyntax { get; set; }
        public INamedTypeSymbol? DataContextSymbol { get; set; }

        public HashSet<string> Hovers { get; set; } = [];
        public List<Tuple<string, string, IEnumerable<string>>> Themes { get; set; } = [];

        private void ReadContextConfigParams(INamedTypeSymbol fieldSymbol)
        {
            var attributeData = fieldSymbol.GetAttributes()
                .FirstOrDefault(ad => ad.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == $"{NAMESPACE_CONFIG}DataContextConfigAttribute");
            if (attributeData != null)
            {
                var name = (string)attributeData.ConstructorArguments[0].Value!;
                var validation = (string)attributeData.ConstructorArguments[1].Value!;
                var targetSymbol = AnalizeHelper.FindTargetTypeSymbol(Compilation, name, validation);
                if (targetSymbol != null)
                {
                    DataContextSymbol = targetSymbol;
                    var targetSyntax = AnalizeHelper.FindTargetClassSyntax(targetSymbol);
                    if (targetSyntax != null)
                    {
                        DataContextSyntax = targetSyntax;
                    }
                }
            }
        }
        private bool IsUIElement(INamedTypeSymbol? symbol)
        {
            if (symbol == null)
                return false;

            // 检查当前类型是否为System.Windows.UIElement
            if (symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Windows.UIElement")
                return true;

            // 递归检查基类
            return IsUIElement(symbol.BaseType);
        }
        private HashSet<string> GetHoverAttributesTexts()
        {
            HashSet<string> attributes = [];

            var stringArrays = Symbol.GetAttributes()
                .Where(a => a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == FULLNAME_HOVERCONFIG)
                .SelectMany(a =>
                {
                    // 获取构造函数的第一个参数的值
                    var argument = a.ConstructorArguments[0];

                    // 如果是数组，Values会包含数组元素
                    if (argument.Kind == TypedConstantKind.Array)
                    {
                        return argument.Values.Select(v => v.Value?.ToString());
                    }
                    // 如果是params参数直接传递的单个值
                    else if (argument.Kind == TypedConstantKind.Primitive)
                    {
                        return new[] { argument.Value?.ToString() };
                    }

                    return Enumerable.Empty<string>();
                })
                .Where(s => s != null);

            attributes.UnionWith(stringArrays!);

            return attributes;
        }
        private List<Tuple<string, string, IEnumerable<string>>> GetThemeAttributesTexts()
        {
            List<Tuple<string, string, IEnumerable<string>>> result = new();
            foreach (var attribute in Symbol.GetAttributes())
            {
                if (attribute.AttributeClass == null)
                    continue;

                if (attribute.AttributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == FULLNAME_THEMECONFIG)
                {
                    // 获取特性的构造函数参数
                    var constructorArguments = attribute.ConstructorArguments;
                    if (constructorArguments.Length < 2)
                        continue;

                    // 获取前两个参数的实际值
                    string firstParam = GetArgumentValue(constructorArguments[0]);
                    string secondParam = GetArgumentValue(constructorArguments[1]);

                    // 获取剩余参数的文本表示（保持原有逻辑）
                    var attributeSyntax = attribute.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax;
                    if (attributeSyntax == null)
                        continue;

                    var argumentsText = (attributeSyntax.ArgumentList?.ToString() ?? string.Empty).Split(',');
                    if (argumentsText.Length >= 3)
                    {
                        var constructors = argumentsText.Skip(2);
                        var unit = Tuple.Create(firstParam, secondParam, constructors);
                        result.Add(unit);
                    }
                }
            }
            return result;
        }
        private string GetArgumentValue(TypedConstant argument)
        {
            if (argument.IsNull)
                return string.Empty;

            switch (argument.Kind)
            {
                case TypedConstantKind.Primitive:
                    return argument.Value?.ToString() ?? string.Empty;
                case TypedConstantKind.Enum:
                    return argument.Value?.ToString() ?? string.Empty;
                case TypedConstantKind.Type:
                    return (argument.Value as ITypeSymbol)?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty;
                case TypedConstantKind.Array:
                    return string.Join(",", argument.Values.Select(GetArgumentValue));
                default:
                    return string.Empty;
            }
        }
        private void LoadPropertySymbolAtTree(INamedTypeSymbol? symbol, List<IPropertySymbol> properties)
        {
            if (symbol is null)
            {
                return;
            }

            foreach (var property in symbol.GetMembers().OfType<IPropertySymbol>())
            {
                properties.Add(property);
            }

            LoadPropertySymbolAtTree(symbol.BaseType, properties);
        }

        public string Generate()
        {
            var builder = new StringBuilder();

            builder.AppendLine(GenerateUsing());
            builder.AppendLine(GenerateNamespace());
            builder.AppendLine(GeneratePartialClass());
            builder.AppendLine(GenerateITA());
            builder.AppendLine(GenerateView());
            builder.AppendLine(GenerateModelReader());
            builder.AppendLine(GenerateInitialize());
            builder.AppendLine(GenerateHoverDependencyPropertiesFromViewModel());
            builder.AppendLine(GenerateDependencyPropertiesFromViewModel());
            builder.AppendLine(GenerateEnd());

            return builder.ToString();
        }
        public string GeneratePartialClass()
        {
            StringBuilder sourceBuilder = new();
            string share = $"{Syntax.Modifiers} class {Syntax.Identifier.Text}";

            var list = new List<string>();

            if (IsAop)
            {
                list.Add($"{NAMESPACE_AOP}{AnalizeHelper.GetInterfaceName(Syntax)}");
            }
            if (IsDynamicTheme)
            {
                list.Add($"{NAMESPACE_THEME}IThemeApplied");
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
        public string GenerateModelReader()
        {
            var fullTypeName = DataContextSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (string.IsNullOrEmpty(fullTypeName) || DataContextSyntax is null || DataContextSymbol is null)
            {
                return string.Empty;
            }

            var vmroslyn = new ViewModelRoslyn(DataContextSyntax, DataContextSymbol, Compilation);

            if (string.IsNullOrEmpty(vmroslyn.ModelTypeName) || string.IsNullOrEmpty(vmroslyn.ModelReaderName))
            {
                return string.Empty;
            }

            var sourceBuilder = new StringBuilder();
            sourceBuilder.AppendLine($"      public {vmroslyn.ModelTypeName} To{vmroslyn.ModelReaderName}()");
            sourceBuilder.AppendLine("      {");
            sourceBuilder.AppendLine($"         return (({fullTypeName})DataContext).To{vmroslyn.ModelReaderName}();");
            sourceBuilder.AppendLine("      }");
            return sourceBuilder.ToString();
        }
        public string GenerateInitialize()
        {
            if (DataContextSyntax is null || DataContextSymbol is null)
            {
                return string.Empty;
            }

            var sourceBuilder = new StringBuilder();

            foreach (var fieldRoslyn in new ViewModelRoslyn(DataContextSyntax, DataContextSymbol, Compilation).FieldRoslyns.Where(f => f.CanDependency))
            {
                sourceBuilder.AppendLine(fieldRoslyn.GenerateInitializeFunction(Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            }

            return sourceBuilder.ToString();
        }
        public string GenerateView()
        {
            StringBuilder sourceBuilder = new();
            List<IFactory> factories = [];

            List<IPropertySymbol> properties = [];
            LoadPropertySymbolAtTree(Symbol, properties);

            var themeTexts = GetThemeAttributesTexts();
            var hoverTexts = GetHoverAttributesTexts();

            var themeGroups = themeTexts.GroupBy(tuple => tuple.Item1);

            foreach (var group in themeGroups) // 添加代理属性
            {
                var symbol = properties.FirstOrDefault(s => s.Name == group.Key);
                if (symbol is null) continue;

                // 代理
                var configs = group.ToArray();
                var p_factory = new PropertyFactory(
                    "public",
                    symbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    symbol.Name,
                    TAG_PROXY + symbol.Name,
                    isView: true);
                foreach (var config in configs)
                {
                    p_factory.AttributeBody.Add($"{config.Item2}({string.Join(",", config.Item3)}");
                }
                factories.Add(p_factory);

                // 悬停与主题
                if (hoverTexts.Contains(group.Key))
                {

                }
                else
                {
                    //DependencyPropertyFactory hoveredDP = new(
                    //    Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    //    symbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    //    symbol.Name,
                    //    );
                }
            }

            foreach (var factory in factories)
            {
                sourceBuilder.AppendLine(factory.Generate());
            }

            return sourceBuilder.ToString();
        }
        public string GenerateHoverDependencyPropertiesFromViewModel()
        {
            if (DataContextSymbol is null) return string.Empty;

            var hoverables = DataContextSymbol.GetMembers().OfType<IFieldSymbol>().Select(f => new FieldRoslyn(f)).Where(fr => fr.CanHover && fr.CanDependency).ToArray();

            var localTypeName = Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var vmTypeName = DataContextSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            var sourceBuilder = new StringBuilder();

            foreach (var fieldRoslyn in hoverables)
            {
                if (IsDynamicTheme && fieldRoslyn.ThemeAttributes.Count > 0)
                {
                    foreach (var themeText in fieldRoslyn.ThemeAttributes.Select(t => AnalizeHelper.ExtractThemeName(t)))
                    {
                        sourceBuilder.AppendLine($$"""
                                   public {{fieldRoslyn.TypeName}} {{themeText}}Hovered{{fieldRoslyn.PropertyName}}
                                   {
                                      get => ({{fieldRoslyn.TypeName}})GetValue({{themeText}}Hovered{{fieldRoslyn.PropertyName}}Property);
                                      set => SetValue({{themeText}}Hovered{{fieldRoslyn.PropertyName}}Property, value);
                                   }
                                   public static readonly {{NAMESPACE_DP}}DependencyProperty {{themeText}}Hovered{{fieldRoslyn.PropertyName}}Property =
                                      {{NAMESPACE_DP}}DependencyProperty.Register(
                                      nameof({{themeText}}Hovered{{fieldRoslyn.PropertyName}}),
                                      typeof({{fieldRoslyn.TypeName}}),
                                      typeof({{localTypeName}}),
                                      new {{NAMESPACE_DP}}PropertyMetadata({{fieldRoslyn.Initial.InitialTextParse()}}, _innerRun{{themeText}}Hovered{{fieldRoslyn.PropertyName}}Changed));   
                                   public static void _innerRun{{themeText}}Hovered{{fieldRoslyn.PropertyName}}Changed({{NAMESPACE_DP}}DependencyObject d, {{NAMESPACE_DP}}DependencyPropertyChangedEventArgs e)
                                   {
                                      if (d is {{localTypeName}} control && control.DataContext is {{vmTypeName}} viewModel)
                                      {
                                         viewModel.{{themeText}}Hovered{{fieldRoslyn.PropertyName}} = ({{fieldRoslyn.TypeName}})e.NewValue;
                                      }
                                   }
                            """);
                        sourceBuilder.AppendLine($$"""
                                   public {{fieldRoslyn.TypeName}} {{themeText}}NoHovered{{fieldRoslyn.PropertyName}}
                                   {
                                      get => ({{fieldRoslyn.TypeName}})GetValue({{themeText}}NoHovered{{fieldRoslyn.PropertyName}}Property);
                                      set => SetValue({{themeText}}NoHovered{{fieldRoslyn.PropertyName}}Property, value);
                                   }
                                   public static readonly {{NAMESPACE_DP}}DependencyProperty {{themeText}}NoHovered{{fieldRoslyn.PropertyName}}Property =
                                      {{NAMESPACE_DP}}DependencyProperty.Register(
                                      nameof({{themeText}}NoHovered{{fieldRoslyn.PropertyName}}),
                                      typeof({{fieldRoslyn.TypeName}}),
                                      typeof({{localTypeName}}),
                                      new {{NAMESPACE_DP}}PropertyMetadata({{fieldRoslyn.Initial.InitialTextParse()}}, _innerRun{{themeText}}NoHovered{{fieldRoslyn.PropertyName}}Changed));   
                                   public static void _innerRun{{themeText}}NoHovered{{fieldRoslyn.PropertyName}}Changed({{NAMESPACE_DP}}DependencyObject d, {{NAMESPACE_DP}}DependencyPropertyChangedEventArgs e)
                                   {
                                      if (d is {{localTypeName}} control && control.DataContext is {{vmTypeName}} viewModel)
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
                                      get => ({{fieldRoslyn.TypeName}})GetValue(Hovered{{fieldRoslyn.PropertyName}}Property);
                                      set => SetValue(Hovered{{fieldRoslyn.PropertyName}}Property, value);
                                   }
                                   public static readonly {{NAMESPACE_DP}}DependencyProperty Hovered{{fieldRoslyn.PropertyName}}Property =
                                      {{NAMESPACE_DP}}DependencyProperty.Register(
                                      nameof(Hovered{{fieldRoslyn.PropertyName}}),
                                      typeof({{fieldRoslyn.TypeName}}),
                                      typeof({{localTypeName}}),
                                      new {{NAMESPACE_DP}}PropertyMetadata({{fieldRoslyn.Initial.InitialTextParse()}}, _innerRunHovered{{fieldRoslyn.PropertyName}}Changed));   
                                   private static void _innerRunHovered{{fieldRoslyn.PropertyName}}Changed({{NAMESPACE_DP}}DependencyObject d, {{NAMESPACE_DP}}DependencyPropertyChangedEventArgs e)
                                   {
                                      if (d is {{localTypeName}} control && control.DataContext is {{vmTypeName}} viewModel)
                                      {
                                         viewModel.Hovered{{fieldRoslyn.PropertyName}} = ({{fieldRoslyn.TypeName}})e.NewValue;
                                      }
                                   }
                            """);
                    sourceBuilder.AppendLine($$"""
                                   public {{fieldRoslyn.TypeName}} NoHovered{{fieldRoslyn.PropertyName}}
                                   {
                                      get => ({{fieldRoslyn.TypeName}})GetValue(Hovered{{fieldRoslyn.PropertyName}}Property);
                                      set => SetValue(NoHovered{{fieldRoslyn.PropertyName}}Property, value);
                                   }
                                   public static readonly {{NAMESPACE_DP}}DependencyProperty NoHovered{{fieldRoslyn.PropertyName}}Property =
                                      {{NAMESPACE_DP}}DependencyProperty.Register(
                                      nameof(NoHovered{{fieldRoslyn.PropertyName}}),
                                      typeof({{fieldRoslyn.TypeName}}),
                                      typeof({{localTypeName}}),
                                      new {{NAMESPACE_DP}}PropertyMetadata({{fieldRoslyn.Initial.InitialTextParse()}}, _innerRunNoHovered{{fieldRoslyn.PropertyName}}Changed));   
                                   private static void _innerRunNoHovered{{fieldRoslyn.PropertyName}}Changed({{NAMESPACE_DP}}DependencyObject d, {{NAMESPACE_DP}}DependencyPropertyChangedEventArgs e)
                                   {
                                      if (d is {{localTypeName}} control && control.DataContext is {{vmTypeName}} viewModel)
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
        public string GenerateDependencyPropertiesFromViewModel()
        {
            if (DataContextSymbol is null) return string.Empty;

            var unhoverables = DataContextSymbol.GetMembers().OfType<IFieldSymbol>().Select(f => new FieldRoslyn(f)).Where(fr => !fr.CanHover && fr.CanDependency).ToArray();

            var localTypeName = Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var vmTypeName = DataContextSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            var sourceBuilder = new StringBuilder();

            foreach (var fieldRoslyn in unhoverables)
            {
                if (fieldRoslyn.ThemeAttributes.Count > 0)
                {
                    foreach (var (fullattName, attName) in fieldRoslyn.ThemeAttributes.Select(t => (t.Split('(')[0], AnalizeHelper.ExtractThemeName(t))))
                    {
                        sourceBuilder.AppendLine($$"""
                                   public {{fieldRoslyn.TypeName}} {{attName}}{{fieldRoslyn.PropertyName}}
                                   {
                                      get => ({{fieldRoslyn.TypeName}})GetValue({{attName}}{{fieldRoslyn.PropertyName}}Property);
                                      set => SetValue({{attName}}{{fieldRoslyn.PropertyName}}Property, value);
                                   }
                                   public static readonly {{NAMESPACE_DP}}DependencyProperty {{attName}}{{fieldRoslyn.PropertyName}}Property =
                                      {{NAMESPACE_DP}}DependencyProperty.Register(
                                      nameof({{attName}}{{fieldRoslyn.PropertyName}}),
                                      typeof({{fieldRoslyn.TypeName}}),
                                      typeof({{localTypeName}}),
                                      new {{NAMESPACE_DP}}PropertyMetadata({{fieldRoslyn.Initial.InitialTextParse()}}, _innerRun{{attName}}{{fieldRoslyn.PropertyName}}Changed));   
                                   public static void _innerRun{{attName}}{{fieldRoslyn.PropertyName}}Changed({{NAMESPACE_DP}}DependencyObject d, {{NAMESPACE_DP}}DependencyPropertyChangedEventArgs e)
                                   {
                                      if (d is {{localTypeName}} control && control.DataContext is {{vmTypeName}} viewModel)
                                      {
                                         if(viewModel is MinimalisticWPF.StructuralDesign.Theme.IThemeApplied theme)
                                         {
                                            {{(fieldRoslyn.CanIsolated ? $"{NAMESPACE_CONSTRUCTOR}DynamicTheme.SetIsolatedValue(theme,typeof({fullattName}),\"{fieldRoslyn.PropertyName}\",e.NewValue);" : $"{NAMESPACE_CONSTRUCTOR}DynamicTheme.SetSharedValue(typeof({Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}),typeof({fullattName}),\"{fieldRoslyn.PropertyName}\",e.NewValue);")}}
                                            if(theme.CurrentTheme?.Name == "{{attName}}")
                                            {
                                               viewModel.{{fieldRoslyn.PropertyName}} = ({{fieldRoslyn.TypeName}})e.NewValue;
                                            }            
                                         }
                                         control._inner{{attName}}{{fieldRoslyn.PropertyName}}Changed(({{fieldRoslyn.TypeName}})e.OldValue ,({{fieldRoslyn.TypeName}})e.NewValue);
                                      }
                                   }
                                   public void _inner{{attName}}{{fieldRoslyn.PropertyName}}Changed({{fieldRoslyn.TypeName}} oldValue, {{fieldRoslyn.TypeName}} newValue)
                                   {
                                      On{{attName}}{{fieldRoslyn.PropertyName}}Changed(oldValue ,newValue);
                                   }
                                   partial void On{{attName}}{{fieldRoslyn.PropertyName}}Changed({{fieldRoslyn.TypeName}} oldValue, {{fieldRoslyn.TypeName}} newValue);
                            """);
                    }
                }
                sourceBuilder.AppendLine($$"""
                                   public {{fieldRoslyn.TypeName}} {{fieldRoslyn.PropertyName}}
                                   {
                                      get => ({{fieldRoslyn.TypeName}})GetValue({{fieldRoslyn.PropertyName}}Property);
                                      set => SetValue({{fieldRoslyn.PropertyName}}Property, value);
                                   }
                                   public static readonly {{NAMESPACE_DP}}DependencyProperty {{fieldRoslyn.PropertyName}}Property =
                                      {{NAMESPACE_DP}}DependencyProperty.Register(
                                      nameof({{fieldRoslyn.PropertyName}}),
                                      typeof({{fieldRoslyn.TypeName}}),
                                      typeof({{localTypeName}}),
                                      new {{NAMESPACE_DP}}PropertyMetadata({{fieldRoslyn.Initial.InitialTextParse()}}, _innerRun{{fieldRoslyn.PropertyName}}Changed));   
                                   public static void _innerRun{{fieldRoslyn.PropertyName}}Changed({{NAMESPACE_DP}}DependencyObject d, {{NAMESPACE_DP}}DependencyPropertyChangedEventArgs e)
                                   {
                                      if (d is {{localTypeName}} control && control.DataContext is {{vmTypeName}} viewModel)
                                      {
                                         viewModel.{{fieldRoslyn.PropertyName}} = ({{fieldRoslyn.TypeName}})e.NewValue;
                                         control._inner{{fieldRoslyn.PropertyName}}Changed(({{fieldRoslyn.TypeName}})e.OldValue ,({{fieldRoslyn.TypeName}})e.NewValue);
                                      }
                                   }
                                   public void _inner{{fieldRoslyn.PropertyName}}Changed({{fieldRoslyn.TypeName}} oldValue, {{fieldRoslyn.TypeName}} newValue)
                                   {
                                      On{{fieldRoslyn.PropertyName}}Changed(oldValue ,newValue);
                                   }
                                   partial void On{{fieldRoslyn.PropertyName}}Changed({{fieldRoslyn.TypeName}} oldValue, {{fieldRoslyn.TypeName}} newValue);
                            """);
            }

            return sourceBuilder.ToString();
        }
    }
}
