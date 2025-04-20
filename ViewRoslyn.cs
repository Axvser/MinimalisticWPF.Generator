using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MinimalisticWPF.Generator.Factory;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace MinimalisticWPF.Generator
{
    internal class ViewRoslyn : ClassRoslyn
    {
        const string NAMESPACE_CONFIG = "global::MinimalisticWPF.";
        const string FULLNAME_THEMECONFIG = "global::MinimalisticWPF.ThemeAttribute";
        const string FULLNAME_HOVERCONFIG = "global::MinimalisticWPF.HoverAttribute";
        const string FULLNAME_CLICKCONFIG = "global::MinimalisticWPF.ClickModuleAttribute";
        const string FULLNAME_MONOCONFIG = "global::MinimalisticWPF.MonoBehaviourAttribute";
        const string NAMESPACE_MODEL = "global::System.ComponentModel.";
        const string NAME_INITIALIZE = "InitializeComponent";
        const string NAMESPACE_WINDOWS = "global::System.Windows.";
        const string NAMESPACE_CONSTRUCTOR = "global::MinimalisticWPF.";
        const string TAG_PROXY = "_proxy";
        const string NAMESPACE_TRANSITOIN = "global::MinimalisticWPF.TransitionSystem.";

        internal ViewRoslyn(ClassDeclarationSyntax classDeclarationSyntax, INamedTypeSymbol namedTypeSymbol, Compilation compilation) : base(classDeclarationSyntax, namedTypeSymbol, compilation)
        {
            var isui = IsUIElement(namedTypeSymbol);

            if (!isui)
            {
                IsView = false;
                return;
            }

            var hovers = GetHoverAttributesTexts();
            var themes = GetThemeAttributesTexts();
            ReadContextConfigParams(namedTypeSymbol);
            if (DataContextSymbol is not null && DataContextSyntax is not null)
            {
                foreach (var fieldRoslyn in DataContextSymbol.GetMembers().OfType<IFieldSymbol>().Select(f => new FieldRoslyn(f)))
                {
                    hovers.Remove(fieldRoslyn.PropertyName);
                    themes.RemoveAll(t => t.Item1 == fieldRoslyn.PropertyName);
                }
            }
            Hovers = hovers;
            Themes = themes;
            LoadPropertySymbolAtTree(namedTypeSymbol, PropertyTree);
            ReadClickConfig(namedTypeSymbol);
            ReadMonoConfig(namedTypeSymbol);
            IsInitializable = IsInitializeComponentExist(namedTypeSymbol);
            IsView = Hovers.Count > 0 || Themes.Count > 0 || (DataContextSyntax != null && DataContextSymbol != null) || IsClick || IsMono;
            if (IsView)
            {
                IsStyleExist = PropertyTree.Any(p => p.Name == "Style");
                IsHover = IsHover;
            }
        }

        public bool IsView { get; set; } = false;
        public bool IsInitializable { get; set; } = false;
        public bool IsClick { get; set; } = false;
        public bool IsStyleExist { get; set; } = false;
        public bool IsHover { get; set; } = false;

        public bool IsMono { get; set; } = false;
        public double MonoSpan { get; set; } = 17;

        List<IPropertySymbol> PropertyTree { get; set; } = [];

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
        private void ReadClickConfig(INamedTypeSymbol symbol)
        {
            var attributeData = symbol.GetAttributes()
                .FirstOrDefault(ad => ad.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == FULLNAME_CLICKCONFIG);
            if (attributeData != null)
            {
                IsClick = true;
            }
        }
        private void ReadMonoConfig(INamedTypeSymbol symbol)
        {
            var attributeData = symbol.GetAttributes()
                .FirstOrDefault(ad => ad.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == FULLNAME_MONOCONFIG);
            if (attributeData != null)
            {
                IsMono = true;
                MonoSpan = (double)attributeData.ConstructorArguments[0].Value!;
            }
        }
        private static bool IsUIElement(INamedTypeSymbol? symbol)
        {
            if (symbol == null)
                return false;

            // 检查当前类型是否为System.Windows.UIElement
            if (symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Windows.UIElement")
                return true;

            // 递归检查基类
            return IsUIElement(symbol.BaseType);
        }
        private static bool IsInitializeComponentExist(INamedTypeSymbol symbol)
        {
            return symbol.GetMembers()
                .OfType<IMethodSymbol>()
                .Any(m => m.Name == NAME_INITIALIZE && m.Parameters.Length == 0 && m.ReturnsVoid);
        }
        private HashSet<string> GetHoverAttributesTexts()
        {
            // 1. 获取当前类型所属的编译上下文
            var compilation = Compilation;

            // 2. 精确获取 HoverAttribute 的类型（通过全名匹配）
            var hoverAttributeType = compilation.GetTypeByMetadataName("MinimalisticWPF.HoverAttribute");
            if (hoverAttributeType == null)
            {
                return new HashSet<string>(); // 若类型不存在，返回空集合
            }

            // 3. 筛选出所有 HoverAttribute 属性
            var hoverAttributes = Symbol.GetAttributes()
                .Where(attr => attr.AttributeClass == hoverAttributeType)
                .ToList();

            // 4. 收集所有有效的 propertyNames 参数值
            var propertyNames = new HashSet<string>();

            foreach (var attr in hoverAttributes)
            {
                // 4.1 检查构造函数参数是否存在且是数组类型
                if (attr.ConstructorArguments.Length == 0)
                    continue;

                var arg = attr.ConstructorArguments[0];
                if (arg.Kind != TypedConstantKind.Array)
                    continue;

                // 4.2 遍历数组元素，提取字符串值
                foreach (var element in arg.Values)
                {
                    if (element.Value is string propertyName)
                    {
                        propertyNames.Add(propertyName);
                    }
                }
            }

            return propertyNames;
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
            builder.AppendLine(GenerateConstructor());
            builder.AppendLine(GenerateITA());
            builder.AppendLine(GenerateView());
            builder.AppendLine(GenerateModelReader());
            builder.AppendLine(GenerateHover());
            builder.AppendLine(GenerateHoverControl());
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
        public string GenerateITA()
        {
            if (!IsDynamicTheme) return string.Empty;

            var isHover = IsHover;

            StringBuilder sourceBuilder = new();

            sourceBuilder.AppendLine("      private bool _isNewTheme = true;");
            sourceBuilder.AppendLine("      private global::System.Type? _currentTheme = null;");
            sourceBuilder.AppendLine("      public bool IsThemeChanging { get; set; } = false;");
            sourceBuilder.AppendLine($$"""
                      public global::System.Type? CurrentTheme
                      {
                         get => _currentTheme;
                         set
                         {
                            if(value == null || value == _currentTheme) return;
                            _currentTheme = value;
                            _isNewTheme = true;
                         }
                      }
                """);
            sourceBuilder.AppendLine("      public void RunThemeChanging(global::System.Type? oldTheme, global::System.Type newTheme)");
            sourceBuilder.AppendLine("      {");
            sourceBuilder.AppendLine("         if(newTheme == oldTheme) return;");
            sourceBuilder.AppendLine("         OnThemeChanging(oldTheme ,newTheme);");
            sourceBuilder.AppendLine("      }");
            sourceBuilder.AppendLine("      public void RunThemeChanged(global::System.Type? oldTheme, global::System.Type newTheme)");
            sourceBuilder.AppendLine("      {");
            if (isHover)
            {
                sourceBuilder.AppendLine("         UpdateHoverState();");
            }
            sourceBuilder.AppendLine("         if(newTheme == oldTheme) return;");
            sourceBuilder.AppendLine("         OnThemeChanged(oldTheme ,newTheme);");
            sourceBuilder.AppendLine("      }");
            sourceBuilder.AppendLine("      partial void OnThemeChanging(global::System.Type? oldTheme, global::System.Type newTheme);");
            sourceBuilder.AppendLine("      partial void OnThemeChanged(global::System.Type? oldTheme, global::System.Type newTheme);");
            return sourceBuilder.ToString();
        }
        public string GenerateConstructor()
        {
            var acc = AnalizeHelper.GetAccessModifier(Symbol);

            var methods = Symbol.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => m.GetAttributes().Any(att => att.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == $"{NAMESPACE_CONSTRUCTOR}ConstructorAttribute"))
                .ToList();

            var themeGroups = Themes.GroupBy(tuple => tuple.Item1);

            StringBuilder builder = new();
            var strAop = $"{NAMESPACE_AOP}{AnalizeHelper.GetInterfaceName(Syntax)}";
            if (IsAop)
            {
                builder.AppendLine($$"""
                                           public {{strAop}} Proxy { get; private set; }
                                     """);
                builder.AppendLine();
            }

            if (IsMono)
            {
                builder.AppendLine($$"""
                          private global::System.Threading.CancellationTokenSource? cts_mono = null;

                          private bool isactive = true;
                          public bool IsActive
                          {
                              get => isactive;
                              private set => isactive = value;
                          }

                          public async void SetIsActive(bool value)
                          {
                              if (value != IsActive)
                              {
                                  IsActive = value;
                                  if (value)
                                  {
                                      await _inner_Update();
                                  }
                                  else
                                  {
                                      _innerCleanMonoToken();
                                  }
                              }
                          }

                          private async Task _inner_Update()
                          {
                              _innerCleanMonoToken();

                              var newmonocts = new global::System.Threading.CancellationTokenSource();
                              cts_mono = newmonocts;

                              try
                              {
                                 while (!newmonocts.Token.IsCancellationRequested)
                                 {
                                     Update();
                                     LateUpdate();
                                     await Task.Delay({{MonoSpan}},newmonocts.Token);
                                 }
                              }
                              catch (Exception ex)
                              {
                                  global::System.Diagnostics.Debug.WriteLine(ex.Message);
                              }
                              finally
                              {
                                  if (global::System.Threading.Interlocked.CompareExchange(ref cts_mono, null, newmonocts) == newmonocts) newmonocts.Dispose();
                              }
                          }

                          partial void Awake();
                          partial void Start();
                          partial void Update();
                          partial void LateUpdate();

                          private void _innerCleanMonoToken()
                          {
                              var oldCts = Interlocked.Exchange(ref cts_mono, null);
                              if (oldCts != null)
                              {
                                  try { oldCts.Cancel(); } catch { }
                                  oldCts.Dispose();
                              }
                          }

                    """);
            }

            if (IsClick)
            {
                builder.AppendLine($$"""
                                           private int _clickdowntime = 0;
                                           private int _clickuptime = 0;
                                           public event {{NAMESPACE_WINDOWS}}RoutedEventHandler Click;
                                     """);
            }

            builder.AppendLine($"      {acc} {Symbol.Name}()");
            builder.AppendLine("      {");
            if (IsInitializable)
            {
                builder.AppendLine("         InitializeComponent();");
            }
            if (IsClick)
            {
                builder.AppendLine($$"""
                             MouseLeave += (sender, e) =>
                             {
                                 _clickdowntime = 0;
                                 _clickuptime = 0;
                             };
                             MouseLeftButtonDown += (sender, e) =>
                             {
                                 _clickdowntime++;
                                 if (_clickdowntime > 0 && _clickuptime > 0)
                                 {
                                     Click.Invoke(this, e);
                                     _clickdowntime = 0;
                                     _clickuptime = 0;
                                 }
                             };
                             MouseLeftButtonUp += (sender, e) =>
                             {
                                 _clickuptime++;
                                 if (_clickdowntime > 0 && _clickuptime > 0)
                                 {
                                     Click?.Invoke(this, e);
                                     _clickdowntime = 0;
                                     _clickuptime = 0;
                                 }
                             };
                    """);
            }
            if (IsAop)
            {
                builder.AppendLine($"         Proxy = this.CreateProxy<{strAop}>();");
            }
            if (IsDynamicTheme)
            {
                builder.AppendLine($"         {NAMESPACE_CONSTRUCTOR}DynamicTheme.Awake(this);");
            }
            if (IsHover)
            {
                builder.AppendLine($"         HoveredTransition.SetParams(TransitionParams.Hover);");
                builder.AppendLine($"         NoHoveredTransition.SetParams(TransitionParams.Hover);");
            }
            foreach (var method in methods.Where(m => !m.Parameters.Any()))
            {
                builder.AppendLine($"         {method.Name}();");
            }
            if (IsHover)
            {
                builder.AppendLine($$"""
                         HoveredTransition.TransitionParams.Start += () =>
                         {
                             IsHoverChanging = true;
                         };
                         HoveredTransition.TransitionParams.Completed += () =>
                         {
                             IsHoverChanging = false;
                         };
                         NoHoveredTransition.TransitionParams.Start += () =>
                         {
                             IsHoverChanging = true;
                         };
                         NoHoveredTransition.TransitionParams.Completed += () =>
                         {
                             IsHoverChanging = false;
                         };
                """);
            }
            builder.AppendLine("         Loaded += (sender,e) =>");
            builder.AppendLine("         {");
            if (IsDynamicTheme)
            {
                builder.AppendLine($"            CurrentTheme = global::MinimalisticWPF.DynamicTheme.CurrentTheme;");
            }
            LoadHoverValueInitialBody(builder, themeGroups);
            builder.AppendLine("         };");
            if (IsMono)
            {
                builder.AppendLine($"         var isInDesign = {NAMESPACE_MODEL}DesignerProperties.GetIsInDesignMode(this);\r\n");
                builder.AppendLine("         Loaded += async (sender,e) =>");
                builder.AppendLine("         {");
                builder.AppendLine(GetMonoUpdateBody());
                builder.AppendLine("         };");
            }
            if (Symbol.Name == "MainWindow")
            {
                builder.AppendLine($$"""
                                 Closed += (sender, e) =>
                                 {
                                     global::MinimalisticWPF.DynamicTheme.Dispose();
                                 };
                        """);
            }
            if (Hovers.Count > 0)
            {
                builder.AppendLine($$"""
                             MouseEnter += (sender, e) =>
                             {
                                IsHovered = true;
                             };
                             MouseLeave += (sender, e) =>
                             {
                                IsHovered = false;
                             };
                    """);
            }
            if (IsMono)
            {
                builder.AppendLine(GetMonoAwakeBody());
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
                builder.AppendLine($"      {acc} {Symbol.Name}({parameterList})");
                builder.AppendLine("      {");
                if (IsInitializable)
                {
                    builder.AppendLine("         InitializeComponent();");
                }
                if (IsClick)
                {
                    builder.AppendLine($$"""
                             MouseLeave += (sender, e) =>
                             {
                                 _clickdowntime = 0;
                                 _clickuptime = 0;
                             };
                             MouseLeftButtonDown += (sender, e) =>
                             {
                                 _clickdowntime++;
                                 if (_clickdowntime > 0 && _clickuptime > 0)
                                 {
                                     Click.Invoke(this, e);
                                     _clickdowntime = 0;
                                     _clickuptime = 0;
                                 }
                             };
                             MouseLeftButtonUp += (sender, e) =>
                             {
                                 _clickuptime++;
                                 if (_clickdowntime > 0 && _clickuptime > 0)
                                 {
                                     Click?.Invoke(this, e);
                                     _clickdowntime = 0;
                                     _clickuptime = 0;
                                 }
                             };
                    """);
                }
                if (IsAop)
                {
                    builder.AppendLine($"         Proxy = this.CreateProxy<{strAop}>();");
                }
                if (IsDynamicTheme)
                {
                    builder.AppendLine($"         {NAMESPACE_CONSTRUCTOR}DynamicTheme.Awake(this);");
                }
                if (IsHover)
                {
                    builder.AppendLine($"         HoveredTransition.SetParams(TransitionParams.Hover);");
                    builder.AppendLine($"         NoHoveredTransition.SetParams(TransitionParams.Hover);");
                }
                foreach (var method in group)
                {
                    builder.AppendLine($"         {method.Name}({callParameters});");
                }
                if (IsHover)
                {
                    builder.AppendLine($$"""
                         HoveredTransition.TransitionParams.Start += () =>
                         {
                             IsHoverChanging = true;
                         };
                         HoveredTransition.TransitionParams.Completed += () =>
                         {
                             IsHoverChanging = false;
                         };
                         NoHoveredTransition.TransitionParams.Start += () =>
                         {
                             IsHoverChanging = true;
                         };
                         NoHoveredTransition.TransitionParams.Completed += () =>
                         {
                             IsHoverChanging = false;
                         };
                """);
                }
                builder.AppendLine("         Loaded += (sender,e) =>");
                builder.AppendLine("         {");
                if (IsDynamicTheme)
                {
                    builder.AppendLine($"            CurrentTheme = global::MinimalisticWPF.DynamicTheme.CurrentTheme;");
                }
                LoadHoverValueInitialBody(builder, themeGroups);
                builder.AppendLine("         };");
                if (IsMono)
                {
                    builder.AppendLine($"         var isInDesign = {NAMESPACE_MODEL}DesignerProperties.GetIsInDesignMode(this);\r\n");
                    builder.AppendLine("         Loaded += async (sender,e) =>");
                    builder.AppendLine("         {");
                    builder.AppendLine(GetMonoUpdateBody());
                    builder.AppendLine("         };");
                }
                if (Symbol.Name == "MainWindow")
                {
                    builder.AppendLine($$"""
                                 Closed += (sender, e) =>
                                 {
                                     global::MinimalisticWPF.DynamicTheme.Dispose();
                                 };
                        """);
                }
                if (Hovers.Count > 0)
                {
                    builder.AppendLine($$"""
                             MouseEnter += (sender, e) =>
                             {
                                IsHovered = true;
                             };
                             MouseLeave += (sender, e) =>
                             {
                                IsHovered = false;
                             };
                    """);
                }
                if (IsMono)
                {

                    builder.AppendLine(GetMonoAwakeBody());
                }
                builder.AppendLine("      }");
            }

            return builder.ToString();
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

            var themeGroups = Themes.GroupBy(tuple => tuple.Item1);

            foreach (var group in themeGroups) // 添加代理属性
            {
                var symbol = PropertyTree.FirstOrDefault(s => s.Name == group.Key);
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

                // 主题修改入口
                if (!Hovers.Contains(group.Key))
                {
                    foreach (var config in configs)
                    {
                        var dp_factory = new DependencyPropertyFactory(
                        Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        symbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        $"{AnalizeHelper.ExtractThemeName(config.Item2)}{symbol.Name}",
                        AnalizeHelper.GetDefaultInitialText(symbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                        dp_factory.SetterBody.Add($"      global::MinimalisticWPF.DynamicTheme.SetIsolatedValue(target,typeof({config.Item2}),\"{TAG_PROXY}{config.Item1}\",newValue);");
                        factories.Add(dp_factory);
                    }
                }
            }

            foreach (var factory in factories)
            {
                sourceBuilder.AppendLine(factory.Generate());
            }

            return sourceBuilder.ToString();
        }
        public string GenerateHoverControl()
        {
            if (Hovers.Count == 0) return string.Empty;

            var hoverables = Hovers.Select(n => PropertyTree.FirstOrDefault(p => p.Name == n)).Where(s => s is not null).ToList();
            var themeGroups = Themes.GroupBy(tuple => tuple.Item1).Where(g => hoverables.Any(h => h.Name == g.Key)).ToList();

            StringBuilder sourceBuilder = new();

            if (IsDynamicTheme)
            {
                sourceBuilder.AppendLine($$"""
                       private bool _isHovered = false;
                       public bool IsHovered
                       {
                          get => _isHovered;
                          protected set
                          {
                             if(_isHovered != value)
                             {
                                _isHovered = value;
                                if (!IsThemeChanging)
                                {
                                   UpdateHoverState();
                                }
                              }
                           }
                       }
                 """);
            }
            else
            {
                sourceBuilder.AppendLine($$"""
                       private bool _isHovered = false;
                       public bool IsHovered
                       {
                          get => _isHovered;
                          protected set
                          {
                             if(_isHovered != value)
                             {
                                _isHovered = value;
                                UpdateHoverState();
                             }
                          }
                       }
                 """);
            }

            sourceBuilder.AppendLine();

            sourceBuilder.AppendLine($$"""
                      private bool _isHoverChanging = false;
                      public bool IsHoverChanging
                      {
                         get => _isHoverChanging;
                         protected set
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
                      public {{NAMESPACE_TRANSITOIN}}TransitionBoard<{{Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}> HoveredTransition { get; set; } = {{NAMESPACE_TRANSITOIN}}Transition.Create<{{Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}>();
                """);
            sourceBuilder.AppendLine($$"""
                      public {{NAMESPACE_TRANSITOIN}}TransitionBoard<{{Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}> NoHoveredTransition { get; set; } = {{NAMESPACE_TRANSITOIN}}Transition.Create<{{Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}}>();
                """);

            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine("      public global::MinimalisticWPF.TransitionSystem.TransitionScheduler[] _runningHovers { get; protected set; } = global::System.Array.Empty<global::MinimalisticWPF.TransitionSystem.TransitionScheduler>();");
            sourceBuilder.AppendLine();

            //生成主题修改后的动画效果更新函数
            sourceBuilder.AppendLine("      protected virtual void UpdateHoverState()");
            sourceBuilder.AppendLine("      {");
            if (IsDynamicTheme)
            {
                sourceBuilder.AppendLine("         if(_isNewTheme && CurrentTheme != null)");
                sourceBuilder.AppendLine("         {");
                sourceBuilder.AppendLine("             _isNewTheme = false;");
                foreach (var propertySymbol in hoverables)
                {
                    var attributes = themeGroups.FirstOrDefault(tg => tg.Key == propertySymbol.Name);
                    if (attributes is null) continue;

                    if (IsDynamicTheme)
                    {
                        sourceBuilder.AppendLine($"             HoveredTransition.SetProperty(b => b.{propertySymbol.Name}, {propertySymbol.Name}_SelectThemeValue_Hovered(CurrentTheme.Name));");
                        sourceBuilder.AppendLine($"             NoHoveredTransition.SetProperty(b => b.{propertySymbol.Name}, {propertySymbol.Name}_SelectThemeValue_NoHovered(CurrentTheme.Name));");
                    }
                }
                sourceBuilder.AppendLine("         }");
            }
            sourceBuilder.AppendLine($$"""
                         var copy = _runningHovers;
                         foreach (var item in copy)
                         {
                            item.Dispose();
                         }
                         _runningHovers = this.BeginTransitions(IsHovered ? HoveredTransition : NoHoveredTransition);
                """);
            sourceBuilder.AppendLine("      }");
            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine("      protected virtual void UpdateHoverState(global::MinimalisticWPF.TransitionSystem.TransitionParams transitionParam)");
            sourceBuilder.AppendLine("      {");
            if (IsDynamicTheme)
            {
                sourceBuilder.AppendLine("         if(_isNewTheme && CurrentTheme != null)");
                sourceBuilder.AppendLine("         {");
                sourceBuilder.AppendLine("             _isNewTheme = false;");
                foreach (var propertySymbol in hoverables)
                {
                    var attributes = themeGroups.FirstOrDefault(tg => tg.Key == propertySymbol.Name);
                    if (attributes is null) continue;

                    if (IsDynamicTheme)
                    {
                        sourceBuilder.AppendLine($"             HoveredTransition.SetProperty(b => b.{propertySymbol.Name}, {propertySymbol.Name}_SelectThemeValue_Hovered(CurrentTheme.Name));");
                        sourceBuilder.AppendLine($"             NoHoveredTransition.SetProperty(b => b.{propertySymbol.Name}, {propertySymbol.Name}_SelectThemeValue_NoHovered(CurrentTheme.Name));");
                    }
                }
                sourceBuilder.AppendLine("         }");
            }
            sourceBuilder.AppendLine($$"""
                         var copy = _runningHovers;
                         foreach (var item in copy)
                         {
                            item.Dispose();
                         }
                         this.BeginTransition(IsHovered ? HoveredTransition : NoHoveredTransition, transitionParam);
                """);
            sourceBuilder.AppendLine("      }");

            //生成Hovered值选择器
            foreach (var propertySymbol in hoverables)
            {
                var attributes = themeGroups.FirstOrDefault(tg => tg.Key == propertySymbol.Name);
                if (attributes is null) continue;

                if (IsDynamicTheme)
                {
                    sourceBuilder.AppendLine($"      protected {propertySymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {propertySymbol.Name}_SelectThemeValue_Hovered(string themeName)");
                    sourceBuilder.AppendLine("      {");
                    sourceBuilder.AppendLine($"         switch(themeName)");
                    sourceBuilder.AppendLine("         {");
                    foreach (var themeText in attributes)
                    {
                        sourceBuilder.AppendLine($"            case \"{AnalizeHelper.ExtractThemeName(themeText.Item2)}\":");
                        sourceBuilder.AppendLine($"                 return {AnalizeHelper.ExtractThemeName(themeText.Item2)}Hovered{propertySymbol.Name};");
                    }
                    sourceBuilder.AppendLine("         }");
                    sourceBuilder.AppendLine($"         return {propertySymbol.Name};");
                    sourceBuilder.AppendLine("      }");
                }
                sourceBuilder.AppendLine();
            }

            //生成NoHovered值选择器
            foreach (var propertySymbol in hoverables)
            {
                var attributes = themeGroups.FirstOrDefault(tg => tg.Key == propertySymbol.Name);
                if (attributes is null) continue;

                if (IsDynamicTheme)
                {
                    sourceBuilder.AppendLine($"      protected {propertySymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {propertySymbol.Name}_SelectThemeValue_NoHovered(string themeName)");
                    sourceBuilder.AppendLine("      {");
                    sourceBuilder.AppendLine($"         switch(themeName)");
                    sourceBuilder.AppendLine("         {");
                    foreach (var themeText in attributes)
                    {
                        sourceBuilder.AppendLine($"            case \"{AnalizeHelper.ExtractThemeName(themeText.Item2)}\":");
                        sourceBuilder.AppendLine($"                 return {AnalizeHelper.ExtractThemeName(themeText.Item2)}NoHovered{propertySymbol.Name};");
                    }
                    sourceBuilder.AppendLine("         }");
                    sourceBuilder.AppendLine($"         return {propertySymbol.Name};");
                    sourceBuilder.AppendLine("      }");
                }
            }

            return sourceBuilder.ToString();
        }
        public string GenerateHover()
        {
            if (Hovers.Count == 0) return string.Empty;

            List<IFactory> factories = [];

            StringBuilder builder = new();

            var themeGroups = Themes.GroupBy(tuple => tuple.Item1);
            foreach (var hover in Hovers)
            {
                var themeGroup = themeGroups.FirstOrDefault(t => t.Key == hover);
                var propertySymbol = PropertyTree.FirstOrDefault(p => p.Name == hover);
                if (propertySymbol is null) continue;

                if (themeGroup is null)
                {
                    var hoveredName = $"Hovered{hover}";
                    var nohoveredName = $"NoHovered{hover}";
                    var dp_factory1 = new DependencyPropertyFactory(
                        Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        propertySymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        hoveredName,
                        $"{AnalizeHelper.GetDefaultInitialText(propertySymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))}");
                    var dp_factory2 = new DependencyPropertyFactory(
                        Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        propertySymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        nohoveredName,
                        $"{AnalizeHelper.GetDefaultInitialText(propertySymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))}");
                    dp_factory1.SetterBody.Add($"      target.HoveredTransition.SetProperty(x => x.{propertySymbol.Name},newValue);");
                    dp_factory2.SetterBody.Add($"      target.NoHoveredTransition.SetProperty(x => x.{propertySymbol.Name},newValue);");
                    factories.Add(dp_factory1);
                    factories.Add(dp_factory2);
                }
                else
                {
                    foreach (var theme in themeGroup)
                    {
                        var fullName = Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        var typeName = propertySymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        var initialText = AnalizeHelper.GetDefaultInitialText(propertySymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                        var hoveredName = $"{AnalizeHelper.ExtractThemeName(theme.Item2)}Hovered{hover}";
                        var nohoveredName = $"{AnalizeHelper.ExtractThemeName(theme.Item2)}NoHovered{hover}";
                        var dp_factory1 = new DependencyPropertyFactory(
                            fullName,
                            typeName,
                            hoveredName,
                            initialText);
                        var dp_factory2 = new DependencyPropertyFactory(
                            fullName,
                            typeName,
                            nohoveredName,
                            initialText);
                        dp_factory1.SetterBody.Add($$"""
                                  if(target.CurrentTheme == typeof({{theme.Item2}}))
                                        {
                                           target.HoveredTransition.SetProperty(x => x.{{propertySymbol.Name}}, newValue);
                                           if(!target.IsHoverChanging && target.IsHovered && !target.IsThemeChanging)
                                           {
                                              target.{{propertySymbol.Name}} = newValue;
                                           }
                                        }
                            """);
                        dp_factory2.SetterBody.Add($$"""
                                  global::MinimalisticWPF.DynamicTheme.SetIsolatedValue(target,typeof({{theme.Item2}}),"{{TAG_PROXY}}{{propertySymbol.Name}}",newValue);
                            """);
                        dp_factory2.SetterBody.Add($$"""
                                  if(target.CurrentTheme == typeof({{theme.Item2}}))
                                        {
                                           target.NoHoveredTransition.SetProperty(x => x.{{propertySymbol.Name}}, newValue);
                                           if(!target.IsHoverChanging && !target.IsHovered && !target.IsThemeChanging)
                                           {
                                              target.{{propertySymbol.Name}} = newValue;
                                           }
                                        }
                            """);
                        factories.Add(dp_factory1);
                        factories.Add(dp_factory2);
                    }
                }
            }

            foreach (var factory in factories)
            {
                builder.AppendLine(factory.Generate());
            }

            return builder.ToString();
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
                if (fieldRoslyn.ThemeAttributes.Count > 0)
                {
                    foreach (var themeText in fieldRoslyn.ThemeAttributes.Select(t => AnalizeHelper.ExtractThemeName(t)))
                    {
                        sourceBuilder.AppendLine($$"""
                                   public {{fieldRoslyn.TypeName}} {{themeText}}Hovered{{fieldRoslyn.PropertyName}}
                                   {
                                      get => ({{fieldRoslyn.TypeName}})GetValue({{themeText}}Hovered{{fieldRoslyn.PropertyName}}Property);
                                      set => SetValue({{themeText}}Hovered{{fieldRoslyn.PropertyName}}Property, value);
                                   }
                                   public static readonly {{NAMESPACE_WINDOWS}}DependencyProperty {{themeText}}Hovered{{fieldRoslyn.PropertyName}}Property =
                                      {{NAMESPACE_WINDOWS}}DependencyProperty.Register(
                                      nameof({{themeText}}Hovered{{fieldRoslyn.PropertyName}}),
                                      typeof({{fieldRoslyn.TypeName}}),
                                      typeof({{localTypeName}}),
                                      new {{NAMESPACE_WINDOWS}}PropertyMetadata({{fieldRoslyn.Initial.InitialTextParse()}}, _innerRun{{themeText}}Hovered{{fieldRoslyn.PropertyName}}Changed));   
                                   public static void _innerRun{{themeText}}Hovered{{fieldRoslyn.PropertyName}}Changed({{NAMESPACE_WINDOWS}}DependencyObject d, {{NAMESPACE_WINDOWS}}DependencyPropertyChangedEventArgs e)
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
                                   public static readonly {{NAMESPACE_WINDOWS}}DependencyProperty {{themeText}}NoHovered{{fieldRoslyn.PropertyName}}Property =
                                      {{NAMESPACE_WINDOWS}}DependencyProperty.Register(
                                      nameof({{themeText}}NoHovered{{fieldRoslyn.PropertyName}}),
                                      typeof({{fieldRoslyn.TypeName}}),
                                      typeof({{localTypeName}}),
                                      new {{NAMESPACE_WINDOWS}}PropertyMetadata({{fieldRoslyn.Initial.InitialTextParse()}}, _innerRun{{themeText}}NoHovered{{fieldRoslyn.PropertyName}}Changed));   
                                   public static void _innerRun{{themeText}}NoHovered{{fieldRoslyn.PropertyName}}Changed({{NAMESPACE_WINDOWS}}DependencyObject d, {{NAMESPACE_WINDOWS}}DependencyPropertyChangedEventArgs e)
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
                                   public static readonly {{NAMESPACE_WINDOWS}}DependencyProperty Hovered{{fieldRoslyn.PropertyName}}Property =
                                      {{NAMESPACE_WINDOWS}}DependencyProperty.Register(
                                      nameof(Hovered{{fieldRoslyn.PropertyName}}),
                                      typeof({{fieldRoslyn.TypeName}}),
                                      typeof({{localTypeName}}),
                                      new {{NAMESPACE_WINDOWS}}PropertyMetadata({{fieldRoslyn.Initial.InitialTextParse()}}, _innerRunHovered{{fieldRoslyn.PropertyName}}Changed));   
                                   private static void _innerRunHovered{{fieldRoslyn.PropertyName}}Changed({{NAMESPACE_WINDOWS}}DependencyObject d, {{NAMESPACE_WINDOWS}}DependencyPropertyChangedEventArgs e)
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
                                   public static readonly {{NAMESPACE_WINDOWS}}DependencyProperty NoHovered{{fieldRoslyn.PropertyName}}Property =
                                      {{NAMESPACE_WINDOWS}}DependencyProperty.Register(
                                      nameof(NoHovered{{fieldRoslyn.PropertyName}}),
                                      typeof({{fieldRoslyn.TypeName}}),
                                      typeof({{localTypeName}}),
                                      new {{NAMESPACE_WINDOWS}}PropertyMetadata({{fieldRoslyn.Initial.InitialTextParse()}}, _innerRunNoHovered{{fieldRoslyn.PropertyName}}Changed));   
                                   private static void _innerRunNoHovered{{fieldRoslyn.PropertyName}}Changed({{NAMESPACE_WINDOWS}}DependencyObject d, {{NAMESPACE_WINDOWS}}DependencyPropertyChangedEventArgs e)
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
                                   public static readonly {{NAMESPACE_WINDOWS}}DependencyProperty {{attName}}{{fieldRoslyn.PropertyName}}Property =
                                      {{NAMESPACE_WINDOWS}}DependencyProperty.Register(
                                      nameof({{attName}}{{fieldRoslyn.PropertyName}}),
                                      typeof({{fieldRoslyn.TypeName}}),
                                      typeof({{localTypeName}}),
                                      new {{NAMESPACE_WINDOWS}}PropertyMetadata({{fieldRoslyn.Initial.InitialTextParse()}}, _innerRun{{attName}}{{fieldRoslyn.PropertyName}}Changed));   
                                   public static void _innerRun{{attName}}{{fieldRoslyn.PropertyName}}Changed({{NAMESPACE_WINDOWS}}DependencyObject d, {{NAMESPACE_WINDOWS}}DependencyPropertyChangedEventArgs e)
                                   {
                                      if (d is {{localTypeName}} control && control.DataContext is {{vmTypeName}} viewModel)
                                      {
                                         if(viewModel is MinimalisticWPF.StructuralDesign.Theme.IThemeApplied theme)
                                         {
                                            {{NAMESPACE_CONSTRUCTOR}}DynamicTheme.SetIsolatedValue(theme,typeof({{fullattName}}),"{{fieldRoslyn.PropertyName}}",e.NewValue);
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
                else
                {
                    sourceBuilder.AppendLine($$"""
                                   public {{fieldRoslyn.TypeName}} {{fieldRoslyn.PropertyName}}
                                   {
                                      get => ({{fieldRoslyn.TypeName}})GetValue({{fieldRoslyn.PropertyName}}Property);
                                      set => SetValue({{fieldRoslyn.PropertyName}}Property, value);
                                   }
                                   public static readonly {{NAMESPACE_WINDOWS}}DependencyProperty {{fieldRoslyn.PropertyName}}Property =
                                      {{NAMESPACE_WINDOWS}}DependencyProperty.Register(
                                      nameof({{fieldRoslyn.PropertyName}}),
                                      typeof({{fieldRoslyn.TypeName}}),
                                      typeof({{localTypeName}}),
                                      new {{NAMESPACE_WINDOWS}}PropertyMetadata({{fieldRoslyn.Initial.InitialTextParse()}}, _innerRun{{fieldRoslyn.PropertyName}}Changed));   
                                   public static void _innerRun{{fieldRoslyn.PropertyName}}Changed({{NAMESPACE_WINDOWS}}DependencyObject d, {{NAMESPACE_WINDOWS}}DependencyPropertyChangedEventArgs e)
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
            }

            return sourceBuilder.ToString();
        }

        private void LoadHoverValueInitialBody(StringBuilder builder, IEnumerable<IGrouping<string, Tuple<string, string, IEnumerable<string>>>> themeGroups)
        {
            if (!IsStyleExist) return;
            builder.AppendLine($"            var setters = Style?.Setters?.OfType<{NAMESPACE_WINDOWS}Setter>()?.Select(s=>s.Property)?.ToHashSet();");
            builder.AppendLine("            var issetterExsit = setters?.Any() ?? false;");
            foreach (var property in PropertyTree.Where(p => Themes.Any(t => t.Item1 == p.Name)))
            {
                var typeName = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                builder.AppendLine($"""                                  
                                    {property.Name} = issetterExsit ? 
                                    ( 
                                        setters.Contains({property.Name}Property) ? 
                                        {property.Name} : 
                                        ({typeName})(global::MinimalisticWPF.DynamicTheme.GetIsolatedValue(this,global::MinimalisticWPF.DynamicTheme.CurrentTheme,"{TAG_PROXY}{property.Name}")??{property.Name})
                                    ) : ({typeName})(global::MinimalisticWPF.DynamicTheme.GetIsolatedValue(this,global::MinimalisticWPF.DynamicTheme.CurrentTheme,"{TAG_PROXY}{property.Name}")??{property.Name});
                        """);
            }
            foreach (var hover in Hovers)
            {
                var themeGroup = themeGroups.FirstOrDefault(t => t.Key == hover);
                var propertySymbol = PropertyTree.FirstOrDefault(p => p.Name == hover);
                if (propertySymbol is null) continue;
                if (themeGroup is null)
                {
                    var hoveredName = $"Hovered{hover}";
                    builder.AppendLine($"""                                  
                                    {hoveredName} = issetterExsit ? 
                                    ( 
                                        setters.Contains({hoveredName}Property) ? 
                                        {hoveredName} : 
                                        {propertySymbol.Name}
                                    ) : {propertySymbol.Name};
                        """);
                    var nohoveredName = $"NoHovered{hover}";
                    builder.AppendLine($"""                                  
                                    {nohoveredName} = issetterExsit ? 
                                    ( 
                                        setters.Contains({nohoveredName}Property) ? 
                                        {nohoveredName} : 
                                        {propertySymbol.Name}
                                    ) : {propertySymbol.Name};
                        """);
                }
                else
                {
                    foreach (var theme in themeGroup)
                    {
                        var themeName = AnalizeHelper.ExtractThemeName(theme.Item2);
                        var typeName = propertySymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        var hoveredName = $"{themeName}Hovered{hover}";
                        builder.AppendLine($"""                                  
                                    {hoveredName} = issetterExsit ? 
                                    ( 
                                        setters.Contains({hoveredName}Property) ? 
                                        {hoveredName} : 
                                        ({typeName})(global::MinimalisticWPF.DynamicTheme.GetIsolatedValue(this,typeof({theme.Item2}),"{TAG_PROXY}{propertySymbol.Name}")??{propertySymbol.Name})
                                    ) : ({typeName})(global::MinimalisticWPF.DynamicTheme.GetIsolatedValue(this,typeof({theme.Item2}),"{TAG_PROXY}{propertySymbol.Name}")??{propertySymbol.Name});
                        """);
                        var nohoveredName = $"{themeName}NoHovered{hover}";
                        builder.AppendLine($"""                                  
                                    {nohoveredName} = issetterExsit ? 
                                    ( 
                                        setters.Contains({nohoveredName}Property) ? 
                                        {nohoveredName} : 
                                        ({typeName})(global::MinimalisticWPF.DynamicTheme.GetIsolatedValue(this,typeof({theme.Item2}),"{TAG_PROXY}{propertySymbol.Name}")??{propertySymbol.Name})
                                    ) : ({typeName})(global::MinimalisticWPF.DynamicTheme.GetIsolatedValue(this,typeof({theme.Item2}),"{TAG_PROXY}{propertySymbol.Name}")??{propertySymbol.Name});
                        """);
                    }
                }
            }
        }
        private string GetMonoUpdateBody()
        {
            return IsMono switch
            {
                true => $$"""
                                if (!isInDesign)
                                {
                                    Start();
                                    await _inner_Update();
                                }
                    """,
                _ => string.Empty
            };
        }
        private string GetMonoAwakeBody()
        {
            return IsMono switch
            {
                true => $$"""
                             if (!isInDesign)
                             {
                                 Awake();
                             }
                    """,
                _ => string.Empty
            };
        }
    }
}
