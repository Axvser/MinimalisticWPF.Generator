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
        const string FULLNAME_THEMECONFIG = "global::MinimalisticWPF.SourceGeneratorMark.ThemeAttribute";
        const string FULLNAME_HOVERCONFIG = "global::MinimalisticWPF.SourceGeneratorMark.HoverAttribute";
        const string FULLNAME_CLICKCONFIG = "global::MinimalisticWPF.SourceGeneratorMark.ClickModuleAttribute";
        const string FULLNAME_STYLE = "global::System.Windows.Style";

        const string NAME_STYLE = "Style";
        const string NAME_INITIALIZE = "InitializeComponent";
        const string TAG_PROXY = "_proxy";

        const string NAMESPACE_WINDOWS = "global::System.Windows.";
        const string NAMESPACE_HOTKEY = "global::MinimalisticWPF.HotKey.";
        const string NAMESPACE_IHOTKEY = "global::MinimalisticWPF.StructuralDesign.HotKey.";
        const string NAMESPACE_SCG = "global::System.Collections.Generic.";
        const string NAMESPACE_TRANSITIONEX = "global::MinimalisticWPF.TransitionSystem.TransitionExtension.";
        const string METHOD_T_D = "global::MinimalisticWPF.Tools.Dependency.DependencyPropertyHelperEx.IsPropertySetInXaml";

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
            Hovers = hovers;
            Themes = themes;
            LoadPropertySymbolAtTree(namedTypeSymbol, PropertyTree);
            ReadClickConfig(namedTypeSymbol);
            IsInitializable = IsInitializeComponentExist(namedTypeSymbol);
            IsHotkey = AnalizeHelper.IsHotKeyClass(namedTypeSymbol);
            IsView = Hovers.Count > 0 || Themes.Count > 0 || IsClick || IsMono || IsHotkey;
            if (IsView)
            {
                IsHover = Hovers.Any();
                IsDynamicTheme = Themes.Any();
            }
        }

        public bool IsView { get; set; } = false;
        public bool IsDynamicTheme { get; set; } = false;
        public bool IsInitializable { get; set; } = false;
        public bool IsClick { get; set; } = false;
        public bool IsHover { get; set; } = false;
        public bool IsHotkey { get; set; } = false;

        List<IPropertySymbol> PropertyTree { get; set; } = [];

        public HashSet<string> Hovers { get; set; } = [];
        public List<Tuple<string, string, IEnumerable<string>>> Themes { get; set; } = [];

        private void ReadClickConfig(INamedTypeSymbol symbol)
        {
            var attributeData = symbol.GetAttributes()
                .FirstOrDefault(ad => ad.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == FULLNAME_CLICKCONFIG);
            if (attributeData != null)
            {
                IsClick = true;
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
            // 1. 筛选出所有 HoverAttribute 属性
            var hoverAttributes = Symbol.GetAttributes()
                .Where(attr => attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == FULLNAME_HOVERCONFIG)
                .ToList();

            // 2. 收集所有有效的 propertyNames 参数值
            var propertyNames = new HashSet<string>();

            foreach (var attr in hoverAttributes)
            {
                // 2.1 检查构造函数参数是否存在且是数组类型
                if (attr.ConstructorArguments.Length == 0)
                    continue;

                var arg = attr.ConstructorArguments[0];
                if (arg.Kind != TypedConstantKind.Array)
                    continue;

                // 2.2 遍历数组元素，提取字符串值
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
            builder.AppendLine(GenerateContext());
            builder.AppendLine(GenerateInitializeMinimalisticWPF());
            builder.AppendLine(GenerateITA());
            builder.AppendLine(GenerateView());
            builder.AppendLine(GenerateHover());
            builder.AppendLine(GenerateHoverControl());
            builder.AppendLine(GenerateHorKeyComponent());
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
                list.Add($"{NAMESPACE_ITHEME}IThemeApplied");
            }
            if (IsHotkey)
            {
                list.Add($"{NAMESPACE_IHOTKEY}IHotKeyComponent");
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

            StringBuilder sourceBuilder = new();

            if (IsHover)
            {
                sourceBuilder.AppendLine("      private bool _isNewTheme = true;");
            }
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
                """);
            if (IsHover)
            {
                sourceBuilder.AppendLine("            _isNewTheme = true;");
            }
            sourceBuilder.AppendLine($$"""
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
            if (IsHover)
            {
                sourceBuilder.AppendLine("         ReLoadHoverTransition();");
                sourceBuilder.AppendLine("         UpdateHoverState();");
            }
            sourceBuilder.AppendLine("         if(newTheme == oldTheme) return;");
            sourceBuilder.AppendLine("         OnThemeChanged(oldTheme ,newTheme);");
            sourceBuilder.AppendLine("      }");
            sourceBuilder.AppendLine("      partial void OnThemeChanging(global::System.Type? oldTheme, global::System.Type newTheme);");
            sourceBuilder.AppendLine("      partial void OnThemeChanged(global::System.Type? oldTheme, global::System.Type newTheme);");
            return sourceBuilder.ToString();
        }
        public string GenerateContext()
        {
            StringBuilder builder = new();
            var strAop = $"{NAMESPACE_AOP}{AnalizeHelper.GetInterfaceName(Syntax)}";
            if (IsAop)
            {
                builder.AppendLine($$"""
                                           public required {{strAop}}? Proxy { get; set; }
                                     """);
                builder.AppendLine();
            }
            var classTypeName = Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (IsMono)
            {
                builder.AppendLine($$"""
                          private global::System.Threading.CancellationTokenSource? cts_mono = null;

                          public bool CanMonoBehaviour
                          {
                              get { return (bool)GetValue(CanMonoBehaviourProperty); }
                              set { SetValue(CanMonoBehaviourProperty, value); }
                          }
                          public static readonly global::System.Windows.DependencyProperty CanMonoBehaviourProperty =
                              global::System.Windows.DependencyProperty.Register("CanMonoBehaviour", typeof(bool), typeof({{classTypeName}}), new global::System.Windows.PropertyMetadata(true, async (dp, e) =>
                              {
                                  if(dp is {{classTypeName}} target)
                                  {
                                      if ((bool)e.NewValue)
                                      {
                                          await target._inner_Update();
                                      }
                                      else
                                      {
                                          target._innerCleanMonoToken();
                                      }
                                  }
                              }));

                          private async Task _inner_Update()
                          {
                              _innerCleanMonoToken();

                              var newmonocts = new global::System.Threading.CancellationTokenSource();
                              cts_mono = newmonocts;

                              try
                              {
                                 if(CanMonoBehaviour) Start();

                                 while (CanMonoBehaviour && !newmonocts.Token.IsCancellationRequested)
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
                                  if (global::System.Threading.Interlocked.CompareExchange(ref cts_mono, null, newmonocts) == newmonocts) 
                                  {
                                      newmonocts.Dispose();
                                  }
                                  ExitMonoBehaviour();
                              }
                          }

                          partial void Awake();
                          partial void Start();
                          partial void Update();
                          partial void LateUpdate();
                          partial void ExitMonoBehaviour();

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
                                           public event {{NAMESPACE_WINDOWS}}RoutedEventHandler? Click;
                                     """);
            }
            return builder.ToString();
        }
        public string GenerateConstructor()
        {
            if (!CanGenerateConstructor) return string.Empty;

            var acc = AnalizeHelper.GetAccessModifier(Symbol);

            var methods = Symbol.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => m.GetAttributes().Any(att => att.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == FULLNAME_CONSTRUCTOR))
                .ToList();

            StringBuilder builder = new();
            builder.AppendLine($"      {acc} {Symbol.Name}()");
            builder.AppendLine("      {");
            if (IsInitializable)
            {
                builder.AppendLine("         InitializeComponent();");
            }
            builder.AppendLine("         InitializeMinimalisticWPF();");
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
                builder.AppendLine($"      {acc} {Symbol.Name}({parameterList})");
                builder.AppendLine("      {");
                if (IsInitializable)
                {
                    builder.AppendLine("         InitializeComponent();");
                }
                builder.AppendLine("         InitializeMinimalisticWPF();");
                foreach (var method in group)
                {
                    builder.AppendLine($"         {method.Name}({callParameters});");
                }
                builder.AppendLine("      }");
            }

            return builder.ToString();
        }
        public string GenerateInitializeMinimalisticWPF()
        {
            var themeGroups = Themes.GroupBy(tuple => tuple.Item1);

            StringBuilder builder = new();
            var strAop = $"{NAMESPACE_AOP}{AnalizeHelper.GetInterfaceName(Syntax)}";
            builder.AppendLine($"      private void InitializeMinimalisticWPF()");
            builder.AppendLine("      {");
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
                                     Click?.Invoke(this, e);
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
                builder.AppendLine($"         Proxy = {NAMESPACE_PROXYEX}CreateProxy<{strAop}>(this);");
            }
            if (IsDynamicTheme)
            {
                builder.AppendLine($"         {NAMESPACE_THEME}DynamicTheme.Awake(this);");
            }
            if (IsHover)
            {
                builder.AppendLine($"         HoveredTransition.SetParams({NAMESPACE_TRANSITOIN}TransitionParams.Hover);");
                builder.AppendLine($"         NoHoveredTransition.SetParams({NAMESPACE_TRANSITOIN}TransitionParams.Hover);");
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
                builder.AppendLine($"             CurrentTheme = {NAMESPACE_THEME}DynamicTheme.CurrentTheme;");
            }
            LoadNoHoverValueInitialBody(builder, themeGroups);
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
                                 SourceInitialized += (s, e) =>
                                 {
                                     global::MinimalisticWPF.HotKey.GlobalHotKey.Awake();
                                 };
                                 Closed += (sender, e) =>
                                 {
                                     global::MinimalisticWPF.Theme.DynamicTheme.Dispose();
                                     global::MinimalisticWPF.HotKey.GlobalHotKey.Dispose();
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

            return builder.ToString();
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
                        dp_factory.SetterBody.Add($"            global::MinimalisticWPF.Theme.DynamicTheme.SetIsolatedValue(target,typeof({config.Item2}),\"{TAG_PROXY}{config.Item1}\",newValue);");
                        dp_factory.SetterBody.Add($$"""
                                        if(target.CurrentTheme == typeof({{config.Item2}}))
                                        {
                                            target.{{symbol.Name}} = newValue;
                                        }
                            """);
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
                          private set
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
                          private set
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
                         private set
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
            sourceBuilder.AppendLine("      public global::MinimalisticWPF.TransitionSystem.TransitionScheduler[] _runningHovers { get; private set; } = global::System.Array.Empty<global::MinimalisticWPF.TransitionSystem.TransitionScheduler>();");
            sourceBuilder.AppendLine();

            //生成主题修改后的动画效果更新函数
            sourceBuilder.AppendLine("      private void ReLoadHoverTransition()");
            sourceBuilder.AppendLine("      {");
            if (IsDynamicTheme)
            {
                sourceBuilder.AppendLine("         if(_isNewTheme && CurrentTheme != null)");
                sourceBuilder.AppendLine("         {");
                sourceBuilder.AppendLine("             _isNewTheme = false;");
                foreach (var propertySymbol in hoverables)
                {
                    if (themeGroups.Any(tg => tg.Key == propertySymbol.Name))
                    {
                        sourceBuilder.AppendLine($"             HoveredTransition.SetProperty(b => b.{propertySymbol.Name}, {propertySymbol.Name}_SelectThemeValue_Hovered(CurrentTheme.Name));");
                        sourceBuilder.AppendLine($"             NoHoveredTransition.SetProperty(b => b.{propertySymbol.Name}, {propertySymbol.Name}_SelectThemeValue_NoHovered(CurrentTheme.Name));");
                    }
                    else
                    {
                        sourceBuilder.AppendLine($"             HoveredTransition.SetProperty(b => b.{propertySymbol.Name}, Hovered{propertySymbol.Name});");
                        sourceBuilder.AppendLine($"             NoHoveredTransition.SetProperty(b => b.{propertySymbol.Name}, NoHovered{propertySymbol.Name});");
                    }
                }
                sourceBuilder.AppendLine("         }");
            }
            sourceBuilder.AppendLine("      }");
            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine("      private void UpdateHoverState()");
            sourceBuilder.AppendLine("      {");
            sourceBuilder.AppendLine($$"""
                         var copy = _runningHovers;
                         foreach (var item in copy)
                         {
                            item.Dispose();
                         }
                         _runningHovers = {{NAMESPACE_TRANSITIONEX}}BeginTransitions(this, IsHovered ? HoveredTransition : NoHoveredTransition);
                """);
            sourceBuilder.AppendLine("      }");
            sourceBuilder.AppendLine();
            //生成Hovered值选择器
            foreach (var propertySymbol in hoverables)
            {
                var attributes = themeGroups.FirstOrDefault(tg => tg.Key == propertySymbol.Name);
                if (attributes is null) continue;

                if (IsDynamicTheme)
                {
                    sourceBuilder.AppendLine($"      private {propertySymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {propertySymbol.Name}_SelectThemeValue_Hovered(string themeName)");
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
                    sourceBuilder.AppendLine($"      private {propertySymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {propertySymbol.Name}_SelectThemeValue_NoHovered(string themeName)");
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
                    dp_factory1.SetterBody.Add($"            target.HoveredTransition.SetProperty(x => x.{propertySymbol.Name},newValue);");
                    dp_factory1.SetterBody.Add($$"""
                                    if(!target.IsHoverChanging && target.IsHovered && !target.IsThemeChanging)
                                    {
                                        target.{{hover}} = newValue;
                                    }
                        """);
                    dp_factory2.SetterBody.Add($"            target.NoHoveredTransition.SetProperty(x => x.{propertySymbol.Name},newValue);");
                    dp_factory2.SetterBody.Add($$"""
                                    if(!target.IsHoverChanging && !target.IsHovered && !target.IsThemeChanging)
                                    {
                                        target.{{hover}} = newValue;
                                    }
                        """);
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
                                        global::MinimalisticWPF.Theme.DynamicTheme.SetIsolatedValue(target,typeof({{theme.Item2}}),"{{TAG_PROXY}}{{propertySymbol.Name}}",newValue);
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
        public string GenerateHorKeyComponent()
        {
            if (!IsHotkey) return string.Empty;
            var className = Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return $$"""
                         private bool _ishotkeyregistered = false;
                         public bool IsHotKeyRegistered
                         {
                             get => _ishotkeyregistered;
                             protected set
                             {
                                _ishotkeyregistered = value;
                                if(value)
                                {
                                   OnSuccessHotKeyRegister();
                                }
                                else
                                {
                                   OnFailedHotKeyRegister();
                                }
                             }
                         }
                         partial void OnFailedHotKeyRegister();
                         partial void OnSuccessHotKeyRegister();

                         public uint RecordedModifiers
                         {
                             get { return (uint)GetValue(RecordedModifiersProperty); }
                             set { SetValue(RecordedModifiersProperty, value); }
                         }
                         public static readonly {{NAMESPACE_WINDOWS}}DependencyProperty RecordedModifiersProperty =
                             {{NAMESPACE_WINDOWS}}DependencyProperty.Register("RecordedModifiers", typeof(uint), typeof({{className}}), new {{NAMESPACE_WINDOWS}}PropertyMetadata(default(uint), Inner_OnModifiersChanged));
                         public static void Inner_OnModifiersChanged({{NAMESPACE_WINDOWS}}DependencyObject d, {{NAMESPACE_WINDOWS}}DependencyPropertyChangedEventArgs e)
                         {
                             if(d is {{className}} target)
                             {
                                global::MinimalisticWPF.HotKey.GlobalHotKey.Unregister((uint)e.OldValue,target.RecordedKey);
                                var id = global::MinimalisticWPF.HotKey.GlobalHotKey.Register(target);
                                target.IsHotKeyRegistered = id != 0 && id != -1;
                                target.OnModifiersChanged((uint)e.OldValue, (uint)e.NewValue);
                             }                            
                         }
                         partial void OnModifiersChanged(uint oldKeys, uint newKeys);

                         public uint RecordedKey
                         {
                             get { return (uint)GetValue(RecordedKeyProperty); }
                             set { SetValue(RecordedKeyProperty, value); }
                         }
                         public static readonly {{NAMESPACE_WINDOWS}}DependencyProperty RecordedKeyProperty =
                             {{NAMESPACE_WINDOWS}}DependencyProperty.Register("RecordedKey", typeof(uint), typeof({{className}}), new {{NAMESPACE_WINDOWS}}PropertyMetadata(default(uint), Inner_OnKeysChanged));
                         public static void Inner_OnKeysChanged({{NAMESPACE_WINDOWS}}DependencyObject d, {{NAMESPACE_WINDOWS}}DependencyPropertyChangedEventArgs e)
                         {
                             if(d is {{className}} target)
                             {
                                global::MinimalisticWPF.HotKey.GlobalHotKey.Unregister(target.RecordedModifiers,(uint)e.OldValue);
                                var id = global::MinimalisticWPF.HotKey.GlobalHotKey.Register(target);
                                target.IsHotKeyRegistered = id != 0 && id != -1;
                                target.OnKeysChanged((uint)e.OldValue, (uint)e.NewValue);
                             }
                         }
                         partial void OnKeysChanged(uint oldKeys, uint newKeys);

                         public event {{NAMESPACE_HOTKEY}}HotKeyEventHandler? HotKeyInvoked;

                         public virtual void InvokeHotKey()
                         {
                             OnHotKeyHandling();
                             HotKeyInvoked?.Invoke(this, new {{NAMESPACE_HOTKEY}}HotKeyEventArgs(RecordedModifiers,RecordedKey));
                             OnHotKeyHandled();
                         }
                         partial void OnHotKeyHandling();
                         partial void OnHotKeyHandled();

                         public virtual void CoverHotKey()
                         {
                             OnHotKeyCovering();
                             modifiers.Clear();
                             key = 0x0000;
                             RecordedModifiers = 0x0000;
                             RecordedKey = 0x0000;
                             OnHotKeyCovered();
                         }
                         partial void OnHotKeyCovering();
                         partial void OnHotKeyCovered();

                         private {{NAMESPACE_SCG}}HashSet<{{NAMESPACE_HOTKEY}}VirtualModifiers> modifiers = [];
                         private {{NAMESPACE_HOTKEY}}VirtualKeys key = 0x0000;

                         protected virtual void OnHotKeyReceived(object sender, global::System.Windows.Input.KeyEventArgs e)
                         {
                             var input = (e.Key == global::System.Windows.Input.Key.System ? e.SystemKey : e.Key);
                             if ({{NAMESPACE_HOTKEY}}HotKeyHelper.WinApiModifiersMapping.TryGetValue(input, out var modifier))
                             {
                                 if (!modifiers.Remove(modifier))
                                 {
                                     modifiers.Add(modifier);
                                 }
                             }
                             else if ({{NAMESPACE_HOTKEY}}HotKeyHelper.WinApiKeysMapping.TryGetValue(input, out var trigger))
                             {
                                 key = trigger;
                             }

                             e.Handled = true;
                             UpdateHotKey();
                         }

                         protected virtual void UpdateHotKey()
                         {
                             OnHotKeyUpdating();
                             RecordedModifiers = {{NAMESPACE_HOTKEY}}HotKeyHelper.CombineModifiers(modifiers);
                             RecordedKey = (uint)key;
                             OnHotKeyUpdated();
                         }
                         partial void OnHotKeyUpdating();
                         partial void OnHotKeyUpdated();
                   """;
        }

        private void LoadNoHoverValueInitialBody(StringBuilder builder, IEnumerable<IGrouping<string, Tuple<string, string, IEnumerable<string>>>> themeGroups)
        {
            foreach (var themeGroup in themeGroups.Where(t => !Hovers.Contains(t.Key)))
            {
                var propertySymbol = PropertyTree.FirstOrDefault(p => p.Name == themeGroup.Key);
                if (propertySymbol is null) continue;
                var typeName = propertySymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var hoveredreplace = typeName.Contains('?') ? string.Empty : $"??{propertySymbol.Name}";
                foreach (var theme in themeGroup)
                {
                    var themeName = AnalizeHelper.ExtractThemeName(theme.Item2);
                    var themedpName = $"{themeName}{theme.Item1}";
                    builder.AppendLine($$"""
                                     if(!{{METHOD_T_D}}(this,{{themedpName}}Property))
                                     {
                                         {{themedpName}} = ({{typeName}})(global::MinimalisticWPF.Theme.DynamicTheme.GetIsolatedValue(this,typeof({{theme.Item2}}),"{{TAG_PROXY}}{{propertySymbol.Name}}"){{hoveredreplace}});
                                     }
                                     _innerOn{{themedpName}}Changed(this,new {{NAMESPACE_WINDOWS}}DependencyPropertyChangedEventArgs({{themedpName}}Property, {{themedpName}}, {{themedpName}}));
                        """);
                }
            }
        }
        private void LoadHoverValueInitialBody(StringBuilder builder, IEnumerable<IGrouping<string, Tuple<string, string, IEnumerable<string>>>> themeGroups)
        {
            foreach (var hover in Hovers)
            {
                var themeGroup = themeGroups.FirstOrDefault(t => t.Key == hover);
                var propertySymbol = PropertyTree.FirstOrDefault(p => p.Name == hover);
                if (propertySymbol is null) continue;
                if (themeGroup is not null)
                {
                    var typeName = propertySymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    foreach (var theme in themeGroup)
                    {
                        var themeName = AnalizeHelper.ExtractThemeName(theme.Item2);
                        var hoveredName = $"{themeName}Hovered{hover}";
                        var hoveredreplace = typeName.Contains('?') ? string.Empty : $"??{propertySymbol.Name}";
                        builder.AppendLine($$""" 
                                      if(!{{METHOD_T_D}}(this,{{hoveredName}}Property))
                                      {
                                          {{hoveredName}} = ({{typeName}})(global::MinimalisticWPF.Theme.DynamicTheme.GetIsolatedValue(this,typeof({{theme.Item2}}),"{{TAG_PROXY}}{{propertySymbol.Name}}"){{hoveredreplace}});
                                      }
                         """);
                        var nohoveredName = $"{themeName}NoHovered{hover}";
                        var nohoveredreplace = typeName.Contains('?') ? string.Empty : $"??{propertySymbol.Name}";
                        builder.AppendLine($$"""                                  
                                      if(!{{METHOD_T_D}}(this,{{nohoveredName}}Property))
                                      {
                                          {{nohoveredName}} = ({{typeName}})(global::MinimalisticWPF.Theme.DynamicTheme.GetIsolatedValue(this,typeof({{theme.Item2}}),"{{TAG_PROXY}}{{propertySymbol.Name}}"){{nohoveredreplace}});
                                      }
                         """);
                        builder.AppendLine($"             _innerOn{hoveredName}Changed(this,new {NAMESPACE_WINDOWS}DependencyPropertyChangedEventArgs({hoveredName}Property, {hoveredName}, {hoveredName}));");
                        builder.AppendLine($"             _innerOn{nohoveredName}Changed(this,new {NAMESPACE_WINDOWS}DependencyPropertyChangedEventArgs({nohoveredName}Property, {nohoveredName}, {nohoveredName}));");
                    }
                }
                else
                {
                    var hoveredName = $"Hovered{hover}";
                    builder.AppendLine($$"""
                                      if(!{{METHOD_T_D}}(this,{{hoveredName}}Property))
                                      {
                                          {{hoveredName}} = {{propertySymbol.Name}};
                                      }
                         """);
                    var nohoveredName = $"NoHovered{hover}";
                    builder.AppendLine($$"""                                 
                                      if(!{{METHOD_T_D}}(this,{{nohoveredName}}Property))
                                      {
                                          {{nohoveredName}} = {{propertySymbol.Name}};
                                      }
                         """);
                    builder.AppendLine($"             _innerOn{hoveredName}Changed(this,new {NAMESPACE_WINDOWS}DependencyPropertyChangedEventArgs({hoveredName}Property, {hoveredName}, {hoveredName}));");
                    builder.AppendLine($"             _innerOn{nohoveredName}Changed(this,new {NAMESPACE_WINDOWS}DependencyPropertyChangedEventArgs({nohoveredName}Property, {nohoveredName}, {nohoveredName}));");
                }
            }
        }
        public override string GetMonoUpdateBody()
        {
            return IsMono switch
            {
                true => $$"""
                                if (!isInDesign && CanMonoBehaviour)
                                {
                                    await _inner_Update();
                                }
                    """,
                _ => string.Empty
            };
        }
        public override string GetMonoAwakeBody()
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
