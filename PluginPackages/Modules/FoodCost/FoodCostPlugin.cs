using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClipDesk.PluginSdk;
using ClipDesk.PluginSdk.Windows;

namespace ClipDesk.Plugin.FoodCost;

public sealed class FoodCostPlugin : IWindowsPluginRenderer
{
    public FrameworkElement CreateBody(WindowsPluginViewContext context)
    {
        var foreground = Brush(context.IsDarkMode ? "#F3F6FA" : "#172033");
        var muted = Brush(context.IsDarkMode ? "#9EABBD" : "#657287");
        var surface = Brush(context.IsDarkMode ? "#1D2838" : "#F4F7FA");
        var card = Brush(context.IsDarkMode ? "#243247" : "#FFFFFF");
        var border = Brush(context.IsDarkMode ? "#38495F" : "#DCE3EB");
        var accent = Brush(context.AccentColor, "#F59E0B");
        var negative = Brush(context.IsDarkMode ? "#FF9A9A" : "#B42318");

        var root = new Grid { Margin = new Thickness(12, 9, 12, 12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var contentHost = new ContentControl();
        Grid.SetRow(contentHost, 1);
        root.Children.Add(contentHost);

        string activeTab = "ingredients";
        string? editingIngredientId = null;
        string? editingDishId = null;
        string? selectedDishId = null;

        void ShowError(PluginCommandResult result)
        {
            if (!result.Succeeded && !string.IsNullOrWhiteSpace(result.Message))
                context.RequestHostAction(new(PluginHostActionKind.ShowMessage, result.Message));
        }

        async Task<PluginCommandResult> Execute(PluginCommand command)
        {
            var result = await context.ExecuteAsync(command, rebuild: false);
            ShowError(result);
            return result;
        }

        void Render()
        {
            contentHost.Content = activeTab == "ingredients"
                ? BuildIngredients()
                : BuildDishes();
        }

        var tabs = PluginToggles.Create(
            context,
            [
                new("ingredients", "Ingredientes"),
                new("dishes", "Pratos / CMV")
            ],
            activeTab,
            value =>
            {
                activeTab = value;
                Render();
            });
        tabs.Margin = new Thickness(0, 0, 0, 9);
        root.Children.Add(tabs);

        FrameworkElement BuildIngredients()
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var stack = new StackPanel();
            scroll.Content = stack;

            var ingredients = FoodCostData.Ingredients(context.State);
            stack.Children.Add(InfoLine(
                ingredients.Count == 0
                    ? "Cadastre quanto você paga e a quantidade comprada. O custo por medida é calculado automaticamente."
                    : $"{ingredients.Count} ingrediente(s) cadastrado(s).",
                muted));

            var edit = ingredients.FirstOrDefault(item => item.Id == editingIngredientId);
            var form = PanelCard(card, border);
            var formStack = new StackPanel();
            form.Child = formStack;

            formStack.Children.Add(SectionTitle(edit is null ? "Novo ingrediente" : "Editar ingrediente", foreground));

            var name = Input(edit?.Name ?? "", "Ex.: farinha de trigo", foreground, surface, border, context.Scale);
            formStack.Children.Add(Field("Nome", name, muted));

            var purchaseGrid = TwoColumns();
            var price = Input(FormatNumber(edit?.PurchasePrice ?? 0), "0,00", foreground, surface, border, context.Scale);
            var quantity = Input(edit is null ? "" : FormatNumber(edit.PurchaseQuantity), "Ex.: 1", foreground, surface, border, context.Scale);
            purchaseGrid.Children.Add(Field("Preço pago (R$)", price, muted));
            var qField = Field("Quantidade comprada", quantity, muted);
            Grid.SetColumn(qField, 1);
            purchaseGrid.Children.Add(qField);
            formStack.Children.Add(purchaseGrid);

            var unit = edit?.PurchaseUnit ?? "kg";
            var unitOptions = new[]
            {
                new PluginDropdownOption("kg", "Quilograma (kg)", "quilo peso"),
                new PluginDropdownOption("g", "Grama (g)", "peso"),
                new PluginDropdownOption("l", "Litro (L)", "volume"),
                new PluginDropdownOption("ml", "Mililitro (ml)", "volume"),
                new PluginDropdownOption("un", "Unidade", "unidade peça")
            };
            var unitButton = PluginDropdowns.Create(context, unitOptions, unit, value => unit = value, "Pesquisar unidade…");
            unitButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            formStack.Children.Add(Field("Unidade da compra", unitButton, muted));

            var density = Input(edit is null || edit.GramsPerMl <= 0 ? "" : FormatNumber(edit.GramsPerMl),
                "Opcional — ex.: 0,53", foreground, surface, border, context.Scale);
            var densityField = Field("Densidade (g/ml)", density, muted);
            densityField.ToolTip = "Opcional. Permite usar medidas de volume em ingredientes comprados por peso e vice-versa.";
            formStack.Children.Add(densityField);

            formStack.Children.Add(InfoLine(
                "Medidas culinárias: 1 c. chá = 5 ml · 1 c. sopa = 15 ml · 1 xícara = 240 ml · 1 copo americano = 190 ml. Para itens comprados por peso, informe a densidade se quiser usar essas medidas.",
                muted));

            var formActions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            if (edit is not null)
            {
                var cancel = PluginButtons.Create(context, "Cancelar");
                cancel.Margin = new Thickness(0, 0, 7, 0);
                cancel.Click += (_, e) =>
                {
                    e.Handled = true;
                    editingIngredientId = null;
                    Render();
                };
                formActions.Children.Add(cancel);
            }
            var save = PluginButtons.Create(context, edit is null ? "Adicionar" : "Salvar", true);
            save.Click += async (_, e) =>
            {
                e.Handled = true;
                var result = await Execute(new PluginCommand("save-ingredient", new Dictionary<string, string>
                {
                    ["id"] = edit?.Id ?? "",
                    ["name"] = name.Text,
                    ["price"] = NormalizeNumber(price.Text),
                    ["quantity"] = NormalizeNumber(quantity.Text),
                    ["unit"] = unit,
                    ["gramsPerMl"] = NormalizeNumber(density.Text, allowEmpty: true)
                }));
                if (!result.Succeeded) return;
                editingIngredientId = null;
                Render();
            };
            formActions.Children.Add(save);
            formStack.Children.Add(formActions);
            stack.Children.Add(form);

            if (ingredients.Count > 0)
            {
                stack.Children.Add(SectionTitle("Ingredientes", foreground, new Thickness(1, 12, 0, 5)));
                foreach (var ingredient in ingredients.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    var rowCard = PanelCard(card, border);
                    rowCard.Margin = new Thickness(0, 0, 0, 7);
                    var row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var text = new StackPanel();
                    text.Children.Add(new TextBlock
                    {
                        Text = ingredient.Name,
                        Foreground = foreground,
                        FontWeight = FontWeights.SemiBold,
                        FontSize = Math.Clamp(14 * context.Scale, 12, 28),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                    var baseQty = FoodCostCalculator.PurchaseBaseQuantity(ingredient);
                    var baseUnit = FoodCostCalculator.BaseUnit(ingredient);
                    var per = baseQty > 0 ? ingredient.PurchasePrice / baseQty : 0;
                    text.Children.Add(new TextBlock
                    {
                        Text = $"R$ {FormatMoney(ingredient.PurchasePrice)} por {FormatNumber(ingredient.PurchaseQuantity)} {FoodCostCalculator.UnitLabel(ingredient.PurchaseUnit)} · R$ {per.ToString("0.####", CultureInfo.CurrentCulture)} / {baseUnit}",
                        Foreground = muted,
                        FontSize = Math.Clamp(11.5 * context.Scale, 10.5, 23),
                        TextWrapping = TextWrapping.Wrap
                    });
                    row.Children.Add(text);

                    var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
                    var editButton = PluginButtons.Create(context, "Editar");
                    editButton.Click += (_, e) =>
                    {
                        e.Handled = true;
                        editingIngredientId = ingredient.Id;
                        Render();
                    };
                    var remove = PluginButtons.Create(context, "Excluir");
                    remove.Margin = new Thickness(6, 0, 0, 0);
                    remove.Click += async (_, e) =>
                    {
                        e.Handled = true;
                        var result = await Execute(new PluginCommand("delete-ingredient",
                            new Dictionary<string, string> { ["id"] = ingredient.Id }));
                        if (result.Succeeded)
                        {
                            if (editingIngredientId == ingredient.Id) editingIngredientId = null;
                            Render();
                        }
                    };
                    actions.Children.Add(editButton);
                    actions.Children.Add(remove);
                    Grid.SetColumn(actions, 1);
                    row.Children.Add(actions);

                    rowCard.Child = row;
                    stack.Children.Add(rowCard);
                }
            }

            return scroll;
        }

        FrameworkElement BuildDishes()
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var stack = new StackPanel();
            scroll.Content = stack;

            var ingredients = FoodCostData.Ingredients(context.State);
            var dishes = FoodCostData.Dishes(context.State);

            var edit = dishes.FirstOrDefault(item => item.Id == editingDishId);
            var form = PanelCard(card, border);
            var formStack = new StackPanel();
            form.Child = formStack;
            formStack.Children.Add(SectionTitle(edit is null ? "Novo prato" : "Editar prato", foreground));

            var name = Input(edit?.Name ?? "", "Ex.: hambúrguer artesanal", foreground, surface, border, context.Scale);
            formStack.Children.Add(Field("Nome do prato", name, muted));

            var priceGrid = TwoColumns();
            var sale = Input(edit is null ? "" : FormatNumber(edit.SalePrice), "0,00", foreground, surface, border, context.Scale);
            var packaging = Input(edit is null ? "" : FormatNumber(edit.PackagingCost), "0,00", foreground, surface, border, context.Scale);
            priceGrid.Children.Add(Field("Preço de venda (R$)", sale, muted));
            var p2 = Field("Embalagem (R$)", packaging, muted);
            Grid.SetColumn(p2, 1);
            priceGrid.Children.Add(p2);
            formStack.Children.Add(priceGrid);

            var costGrid = TwoColumns();
            var delivery = Input(edit is null ? "" : FormatNumber(edit.DeliveryPercent), "Ex.: 12", foreground, surface, border, context.Scale);
            var other = Input(edit is null ? "" : FormatNumber(edit.OtherCost), "0,00", foreground, surface, border, context.Scale);
            costGrid.Children.Add(Field("Delivery (%)", delivery, muted));
            var c2 = Field("Outros custos (R$)", other, muted);
            Grid.SetColumn(c2, 1);
            costGrid.Children.Add(c2);
            formStack.Children.Add(costGrid);

            var formActions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            if (edit is not null)
            {
                var cancel = PluginButtons.Create(context, "Cancelar");
                cancel.Margin = new Thickness(0, 0, 7, 0);
                cancel.Click += (_, e) =>
                {
                    e.Handled = true;
                    editingDishId = null;
                    Render();
                };
                formActions.Children.Add(cancel);
            }
            var save = PluginButtons.Create(context, edit is null ? "Criar prato" : "Salvar", true);
            save.Click += async (_, e) =>
            {
                e.Handled = true;
                var result = await Execute(new PluginCommand("save-dish", new Dictionary<string, string>
                {
                    ["id"] = edit?.Id ?? "",
                    ["name"] = name.Text,
                    ["salePrice"] = NormalizeNumber(sale.Text),
                    ["packagingCost"] = NormalizeNumber(packaging.Text),
                    ["otherCost"] = NormalizeNumber(other.Text),
                    ["deliveryPercent"] = NormalizeNumber(delivery.Text)
                }));
                if (!result.Succeeded) return;
                editingDishId = null;
                if (result.Data is not null && result.Data.TryGetValue("id", out var id))
                    selectedDishId = id;
                Render();
            };
            formActions.Children.Add(save);
            formStack.Children.Add(formActions);
            stack.Children.Add(form);

            if (dishes.Count == 0)
            {
                stack.Children.Add(InfoLine("Crie um prato e depois adicione os ingredientes usados na receita.", muted));
                return scroll;
            }

            stack.Children.Add(SectionTitle("Pratos", foreground, new Thickness(1, 12, 0, 5)));
            foreach (var dish in dishes.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var breakdown = FoodCostCalculator.Calculate(dish, ingredients);
                var rowCard = PanelCard(card, border);
                rowCard.Margin = new Thickness(0, 0, 0, 7);

                var rowStack = new StackPanel();
                var header = new Grid();
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var nameStack = new StackPanel();
                nameStack.Children.Add(new TextBlock
                {
                    Text = dish.Name,
                    Foreground = foreground,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = Math.Clamp(14 * context.Scale, 12, 28)
                });
                nameStack.Children.Add(new TextBlock
                {
                    Text = dish.SalePrice > 0
                        ? $"Venda R$ {FormatMoney(dish.SalePrice)} · {dish.Items.Count} ingrediente(s)"
                        : $"{dish.Items.Count} ingrediente(s) · informe o preço de venda para calcular CMV %",
                    Foreground = muted,
                    FontSize = Math.Clamp(11.5 * context.Scale, 10.5, 23),
                    TextWrapping = TextWrapping.Wrap
                });
                header.Children.Add(nameStack);

                var open = PluginButtons.Create(context, selectedDishId == dish.Id ? "Fechar" : "Abrir", selectedDishId != dish.Id);
                open.Click += (_, e) =>
                {
                    e.Handled = true;
                    selectedDishId = selectedDishId == dish.Id ? null : dish.Id;
                    Render();
                };
                Grid.SetColumn(open, 1);
                header.Children.Add(open);
                rowStack.Children.Add(header);

                var metrics = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                metrics.Children.Add(Metric("Custo", $"R$ {FormatMoney(breakdown.TotalCost)}", foreground, surface));
                metrics.Children.Add(Metric("CMV", dish.SalePrice > 0 ? $"{breakdown.CmvPercent:0.0}%" : "—", accent, surface));
                metrics.Children.Add(Metric("Margem", dish.SalePrice > 0 ? $"{breakdown.MarginPercent:0.0}%" : "—",
                    breakdown.Contribution < 0 ? negative : foreground, surface));
                rowStack.Children.Add(metrics);

                var rowActions = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
                var editButton = PluginButtons.Create(context, "Editar");
                editButton.Click += (_, e) =>
                {
                    e.Handled = true;
                    editingDishId = dish.Id;
                    Render();
                };
                var remove = PluginButtons.Create(context, "Excluir");
                remove.Margin = new Thickness(6, 0, 0, 0);
                remove.Click += async (_, e) =>
                {
                    e.Handled = true;
                    var result = await Execute(new PluginCommand("delete-dish",
                        new Dictionary<string, string> { ["id"] = dish.Id }));
                    if (result.Succeeded)
                    {
                        if (selectedDishId == dish.Id) selectedDishId = null;
                        if (editingDishId == dish.Id) editingDishId = null;
                        Render();
                    }
                };
                rowActions.Children.Add(editButton);
                rowActions.Children.Add(remove);
                rowStack.Children.Add(rowActions);

                rowCard.Child = rowStack;
                stack.Children.Add(rowCard);

                if (selectedDishId == dish.Id)
                    stack.Children.Add(BuildRecipeEditor(dish, ingredients));
            }

            return scroll;
        }

        FrameworkElement BuildRecipeEditor(FoodDish dish, IReadOnlyList<FoodIngredient> ingredients)
        {
            var box = PanelCard(card, accent);
            box.Margin = new Thickness(0, 0, 0, 10);
            var stack = new StackPanel();
            box.Child = stack;
            stack.Children.Add(SectionTitle($"Composição · {dish.Name}", foreground));

            if (ingredients.Count == 0)
            {
                stack.Children.Add(InfoLine("Cadastre pelo menos um ingrediente na primeira aba.", muted));
                return box;
            }

            var ingredientId = ingredients[0].Id;
            var unit = FoodCostCalculator.RecipeUnits(ingredients[0]).FirstOrDefault() ?? "g";
            var ingredientButton = PluginDropdowns.Create(
                context,
                ingredients.Select(item => new PluginDropdownOption(item.Id, item.Name)),
                ingredientId,
                value =>
                {
                    ingredientId = value;
                    var selected = ingredients.First(item => item.Id == value);
                    unit = FoodCostCalculator.RecipeUnits(selected).FirstOrDefault() ?? "";
                    RebuildUnitPicker();
                },
                "Pesquisar ingrediente…");
            ingredientButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            stack.Children.Add(Field("Ingrediente", ingredientButton, muted));

            var unitHost = new ContentControl();
            stack.Children.Add(Field("Medida usada no prato", unitHost, muted));

            void RebuildUnitPicker()
            {
                var ingredient = ingredients.First(item => item.Id == ingredientId);
                var units = FoodCostCalculator.RecipeUnits(ingredient);
                if (!units.Contains(unit)) unit = units.FirstOrDefault() ?? "";
                unitHost.Content = PluginDropdowns.Create(
                    context,
                    units.Select(value => new PluginDropdownOption(value, FoodCostCalculator.UnitLabel(value))),
                    unit,
                    value => unit = value,
                    "Pesquisar medida…");
            }
            RebuildUnitPicker();

            var quantity = Input("", "Ex.: 120", foreground, surface, border, context.Scale);
            stack.Children.Add(Field("Quantidade", quantity, muted));

            var add = PluginButtons.Create(context, "Adicionar ao prato", true);
            add.HorizontalAlignment = HorizontalAlignment.Right;
            add.Margin = new Thickness(0, 7, 0, 0);
            add.Click += async (_, e) =>
            {
                e.Handled = true;
                var result = await Execute(new PluginCommand("save-item", new Dictionary<string, string>
                {
                    ["dishId"] = dish.Id,
                    ["ingredientId"] = ingredientId,
                    ["quantity"] = NormalizeNumber(quantity.Text),
                    ["unit"] = unit
                }));
                if (result.Succeeded) Render();
            };
            stack.Children.Add(add);

            var freshDish = FoodCostData.Dishes(context.State).FirstOrDefault(item => item.Id == dish.Id) ?? dish;
            var freshIngredients = FoodCostData.Ingredients(context.State);
            if (freshDish.Items.Count > 0)
            {
                stack.Children.Add(SectionTitle("Receita", foreground, new Thickness(0, 11, 0, 4)));
                foreach (var item in freshDish.Items)
                {
                    var ingredient = freshIngredients.FirstOrDefault(value => value.Id == item.IngredientId);
                    if (ingredient is null) continue;
                    FoodCostCalculator.TryIngredientCost(ingredient, item.Quantity, item.Unit, out var itemCost);

                    var line = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var label = new TextBlock
                    {
                        Text = $"{ingredient.Name} · {FormatNumber(item.Quantity)} {FoodCostCalculator.UnitLabel(item.Unit)} · R$ {FormatMoney(itemCost)}",
                        Foreground = foreground,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    line.Children.Add(label);
                    var remove = PluginButtons.Create(context, "Remover");
                    remove.Margin = new Thickness(7, 0, 0, 0);
                    remove.Click += async (_, e) =>
                    {
                        e.Handled = true;
                        var result = await Execute(new PluginCommand("delete-item", new Dictionary<string, string>
                        {
                            ["dishId"] = freshDish.Id,
                            ["itemId"] = item.Id
                        }));
                        if (result.Succeeded) Render();
                    };
                    Grid.SetColumn(remove, 1);
                    line.Children.Add(remove);
                    stack.Children.Add(line);
                }
            }

            var breakdown = FoodCostCalculator.Calculate(freshDish, freshIngredients);
            stack.Children.Add(SectionTitle("Resumo do CMV", foreground, new Thickness(0, 12, 0, 5)));
            var summary = new WrapPanel();
            summary.Children.Add(Metric("Ingredientes", $"R$ {FormatMoney(breakdown.IngredientCost)}", foreground, surface));
            summary.Children.Add(Metric("Embalagem", $"R$ {FormatMoney(breakdown.PackagingCost)}", foreground, surface));
            summary.Children.Add(Metric("Delivery", $"R$ {FormatMoney(breakdown.DeliveryCost)}", foreground, surface));
            summary.Children.Add(Metric("Outros", $"R$ {FormatMoney(breakdown.OtherCost)}", foreground, surface));
            summary.Children.Add(Metric("Custo total", $"R$ {FormatMoney(breakdown.TotalCost)}", accent, surface));
            summary.Children.Add(Metric("CMV", freshDish.SalePrice > 0 ? $"{breakdown.CmvPercent:0.0}%" : "—", accent, surface));
            summary.Children.Add(Metric("Resultado", $"R$ {FormatMoney(breakdown.Contribution)}",
                breakdown.Contribution < 0 ? negative : foreground, surface));
            stack.Children.Add(summary);

            foreach (var warning in breakdown.Warnings.Distinct())
                stack.Children.Add(InfoLine(warning, negative));

            return box;
        }

        Render();
        return root;
    }

    private static Border PanelCard(Brush background, Brush border) => new()
    {
        Background = background,
        BorderBrush = border,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(13),
        Padding = new Thickness(11),
        Margin = new Thickness(0, 0, 0, 8),
        Tag = "plugin-interactive"
    };

    private static Grid TwoColumns()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions[0].MinWidth = 80;
        grid.ColumnDefinitions[1].MinWidth = 80;
        return grid;
    }

    private static StackPanel Field(string label, FrameworkElement control, Brush muted)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 7, 7) };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = muted,
            FontSize = 11.5,
            Margin = new Thickness(1, 0, 0, 3)
        });
        panel.Children.Add(control);
        return panel;
    }

    private static TextBox Input(string text, string hint, Brush foreground, Brush background, Brush border, double scale)
    {
        var input = new TextBox
        {
            Text = text,
            ToolTip = hint,
            Foreground = foreground,
            Background = background,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 6, 9, 6),
            MinHeight = Math.Clamp(34 * scale, 30, 70),
            FontSize = Math.Clamp(13 * scale, 11, 28),
            Tag = "plugin-interactive"
        };
        return input;
    }

    private static TextBlock SectionTitle(string text, Brush foreground, Thickness? margin = null) => new()
    {
        Text = text,
        Foreground = foreground,
        FontWeight = FontWeights.SemiBold,
        FontSize = 13.5,
        Margin = margin ?? new Thickness(0, 0, 0, 7),
        TextWrapping = TextWrapping.Wrap
    };

    private static TextBlock InfoLine(string text, Brush color) => new()
    {
        Text = text,
        Foreground = color,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 11.5,
        Margin = new Thickness(1, 2, 1, 8)
    };

    private static Border Metric(string label, string value, Brush valueBrush, Brush background)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = valueBrush,
            Opacity = .72,
            FontSize = 10.5
        });
        stack.Children.Add(new TextBlock
        {
            Text = value,
            Foreground = valueBrush,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13
        });
        return new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 0, 6, 6),
            Child = stack
        };
    }

    private static string NormalizeNumber(string? raw, bool allowEmpty = false)
    {
        raw = raw?.Trim();
        if (string.IsNullOrEmpty(raw))
            return allowEmpty ? "" : "0";
        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out var local))
            return local.ToString(CultureInfo.InvariantCulture);
        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariant))
            return invariant.ToString(CultureInfo.InvariantCulture);
        return raw;
    }

    private static string FormatMoney(decimal value) =>
        value.ToString("N2", CultureInfo.CurrentCulture);

    private static string FormatNumber(decimal value) =>
        value.ToString("0.####", CultureInfo.CurrentCulture);

    private static Brush Brush(string value, string fallback = "#000000")
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); }
        catch { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback)); }
    }
}
