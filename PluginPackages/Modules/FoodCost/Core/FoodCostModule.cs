using System.Globalization;
using System.Text;
using System.Text.Json;
using ClipDesk.PluginSdk;

namespace ClipDesk.Plugin.FoodCost;

public sealed record FoodIngredient(
    string Id,
    string Name,
    decimal PurchasePrice,
    decimal PurchaseQuantity,
    string PurchaseUnit,
    decimal GramsPerMl = 0);

public sealed record FoodRecipeItem(
    string Id,
    string IngredientId,
    decimal Quantity,
    string Unit);

public sealed record FoodDish(
    string Id,
    string Name,
    decimal SalePrice,
    decimal PackagingCost,
    decimal OtherCost,
    decimal DeliveryPercent,
    IReadOnlyList<FoodRecipeItem> Items);

public sealed record FoodCostBreakdown(
    decimal IngredientCost,
    decimal PackagingCost,
    decimal DeliveryCost,
    decimal OtherCost,
    decimal TotalCost,
    decimal CmvPercent,
    decimal Contribution,
    decimal MarginPercent,
    IReadOnlyList<string> Warnings);

public static class FoodCostData
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IReadOnlyList<FoodIngredient> Ingredients(PluginState state) =>
        Read<List<FoodIngredient>>(state.GetString("ingredients")) ?? [];

    public static IReadOnlyList<FoodDish> Dishes(PluginState state) =>
        Read<List<FoodDish>>(state.GetString("dishes")) ?? [];

    public static string SerializeIngredients(IEnumerable<FoodIngredient> items) =>
        JsonSerializer.Serialize(items, JsonOptions);

    public static string SerializeDishes(IEnumerable<FoodDish> items) =>
        JsonSerializer.Serialize(items, JsonOptions);

    private static T? Read<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions); }
        catch (JsonException) { return default; }
    }
}

public static class FoodCostCalculator
{
    private static readonly HashSet<string> MassUnits = new(StringComparer.OrdinalIgnoreCase) { "g", "kg" };
    private static readonly HashSet<string> VolumeUnits = new(StringComparer.OrdinalIgnoreCase)
        { "ml", "l", "tsp", "tbsp", "cup", "glass" };

    public static IReadOnlyList<string> RecipeUnits(FoodIngredient ingredient)
    {
        if (ingredient.PurchaseUnit.Equals("un", StringComparison.OrdinalIgnoreCase))
            return ["un"];

        var purchaseIsMass = MassUnits.Contains(ingredient.PurchaseUnit);
        var purchaseIsVolume = VolumeUnits.Contains(ingredient.PurchaseUnit);
        if (ingredient.GramsPerMl > 0)
            return ["g", "kg", "ml", "l", "tsp", "tbsp", "cup", "glass"];

        return purchaseIsMass
            ? ["g", "kg"]
            : purchaseIsVolume
                ? ["ml", "l", "tsp", "tbsp", "cup", "glass"]
                : [];
    }

    public static FoodCostBreakdown Calculate(FoodDish dish, IReadOnlyList<FoodIngredient> ingredients)
    {
        decimal ingredientCost = 0;
        var warnings = new List<string>();
        var byId = ingredients.ToDictionary(item => item.Id, StringComparer.Ordinal);

        foreach (var item in dish.Items)
        {
            if (!byId.TryGetValue(item.IngredientId, out var ingredient))
            {
                warnings.Add("Há um ingrediente removido neste prato.");
                continue;
            }

            if (!TryIngredientCost(ingredient, item.Quantity, item.Unit, out var cost))
            {
                warnings.Add($"Revise a medida de {ingredient.Name}.");
                continue;
            }

            ingredientCost += cost;
        }

        var deliveryCost = dish.SalePrice > 0
            ? dish.SalePrice * Clamp(dish.DeliveryPercent, 0, 100) / 100m
            : 0m;
        var total = ingredientCost
            + Math.Max(0, dish.PackagingCost)
            + Math.Max(0, dish.OtherCost)
            + deliveryCost;
        var cmv = dish.SalePrice > 0 ? total / dish.SalePrice * 100m : 0m;
        var contribution = dish.SalePrice - total;
        var margin = dish.SalePrice > 0 ? contribution / dish.SalePrice * 100m : 0m;

        return new FoodCostBreakdown(
            RoundMoney(ingredientCost),
            RoundMoney(dish.PackagingCost),
            RoundMoney(deliveryCost),
            RoundMoney(dish.OtherCost),
            RoundMoney(total),
            Math.Round(cmv, 1),
            RoundMoney(contribution),
            Math.Round(margin, 1),
            warnings);
    }

    public static bool TryIngredientCost(
        FoodIngredient ingredient,
        decimal recipeQuantity,
        string recipeUnit,
        out decimal cost)
    {
        cost = 0;
        if (recipeQuantity < 0 || ingredient.PurchasePrice < 0 || ingredient.PurchaseQuantity <= 0)
            return false;

        if (!TryToBaseQuantity(ingredient.PurchaseQuantity, ingredient.PurchaseUnit, ingredient, out var purchasedBase))
            return false;
        if (!TryToBaseQuantity(recipeQuantity, recipeUnit, ingredient, out var recipeBase))
            return false;
        if (purchasedBase <= 0) return false;

        cost = ingredient.PurchasePrice / purchasedBase * recipeBase;
        return true;
    }

    public static string UnitLabel(string unit) => unit switch
    {
        "g" => "g",
        "kg" => "kg",
        "ml" => "ml",
        "l" => "L",
        "un" => "unidade",
        "tsp" => "colher de chá",
        "tbsp" => "colher de sopa",
        "cup" => "xícara",
        "glass" => "copo americano",
        _ => unit
    };

    public static decimal PurchaseBaseQuantity(FoodIngredient ingredient)
    {
        return TryToBaseQuantity(ingredient.PurchaseQuantity, ingredient.PurchaseUnit, ingredient, out var value)
            ? value
            : 0;
    }

    public static string BaseUnit(FoodIngredient ingredient) =>
        ingredient.PurchaseUnit.Equals("un", StringComparison.OrdinalIgnoreCase)
            ? "un"
            : MassUnits.Contains(ingredient.PurchaseUnit) ? "g" : "ml";

    private static bool TryToBaseQuantity(
        decimal quantity,
        string unit,
        FoodIngredient ingredient,
        out decimal baseQuantity)
    {
        baseQuantity = 0;
        if (quantity < 0) return false;

        var purchaseIsUnit = ingredient.PurchaseUnit.Equals("un", StringComparison.OrdinalIgnoreCase);
        var purchaseIsMass = MassUnits.Contains(ingredient.PurchaseUnit);
        var purchaseIsVolume = VolumeUnits.Contains(ingredient.PurchaseUnit);

        if (purchaseIsUnit)
        {
            if (!unit.Equals("un", StringComparison.OrdinalIgnoreCase)) return false;
            baseQuantity = quantity;
            return true;
        }

        var isRecipeMass = MassUnits.Contains(unit);
        var isRecipeVolume = VolumeUnits.Contains(unit);
        if (!isRecipeMass && !isRecipeVolume) return false;

        decimal normalized = unit.ToLowerInvariant() switch
        {
            "kg" => quantity * 1000m,
            "g" => quantity,
            "l" => quantity * 1000m,
            "ml" => quantity,
            "tsp" => quantity * 5m,
            "tbsp" => quantity * 15m,
            "cup" => quantity * 240m,
            "glass" => quantity * 190m,
            _ => -1m
        };
        if (normalized < 0) return false;

        if (purchaseIsMass)
        {
            if (isRecipeMass) { baseQuantity = normalized; return true; }
            if (ingredient.GramsPerMl <= 0) return false;
            baseQuantity = normalized * ingredient.GramsPerMl;
            return true;
        }

        if (purchaseIsVolume)
        {
            if (isRecipeVolume) { baseQuantity = normalized; return true; }
            if (ingredient.GramsPerMl <= 0) return false;
            baseQuantity = normalized / ingredient.GramsPerMl;
            return true;
        }

        return false;
    }

    private static decimal Clamp(decimal value, decimal min, decimal max) =>
        Math.Min(max, Math.Max(min, value));

    private static decimal RoundMoney(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

public sealed class FoodCostModule : IClipDeskPluginModule
{
    private static readonly HashSet<string> PurchaseUnits = new(StringComparer.OrdinalIgnoreCase)
        { "g", "kg", "ml", "l", "un" };

    public string Id => "clipdesk.foodcost";
    public int StateVersion => 1;

    public PluginState CreateDefaultState() => new(new Dictionary<string, string>
    {
        [PluginStateKeys.SchemaVersion] = "1",
        ["ingredients"] = "[]",
        ["dishes"] = "[]"
    });

    public PluginState NormalizeState(PluginState state)
    {
        var values = state.ToDictionary();
        var ingredients = NormalizeIngredients(FoodCostData.Ingredients(state));
        var validIds = ingredients.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var dishes = NormalizeDishes(FoodCostData.Dishes(state), validIds);

        values[PluginStateKeys.SchemaVersion] = "1";
        values["ingredients"] = FoodCostData.SerializeIngredients(ingredients);
        values["dishes"] = FoodCostData.SerializeDishes(dishes);
        return new PluginState(values);
    }

    public ValueTask<PluginCommandResult> ExecuteAsync(
        PluginState state,
        PluginCommand command,
        IPluginExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state = NormalizeState(state);

        var result = command.Name switch
        {
            "save-ingredient" => SaveIngredient(state, command),
            "delete-ingredient" => DeleteIngredient(state, command.Argument("id")),
            "save-dish" => SaveDish(state, command),
            "delete-dish" => DeleteDish(state, command.Argument("id")),
            "save-item" => SaveItem(state, command),
            "delete-item" => DeleteItem(state, command.Argument("dishId"), command.Argument("itemId")),
            _ => PluginCommandResult.Invalid(state, "Comando desconhecido.")
        };
        return ValueTask.FromResult(result);
    }

    public string? GetClipboardText(PluginState state)
    {
        state = NormalizeState(state);
        var ingredients = FoodCostData.Ingredients(state);
        var dishes = FoodCostData.Dishes(state);
        var builder = new StringBuilder();
        builder.AppendLine("CMV de Pratos");
        builder.AppendLine($"{ingredients.Count} ingrediente(s) · {dishes.Count} prato(s)");
        foreach (var dish in dishes)
        {
            var total = FoodCostCalculator.Calculate(dish, ingredients);
            builder.AppendLine($"{dish.Name}: custo {total.TotalCost.ToString("0.00", CultureInfo.InvariantCulture)}; CMV {total.CmvPercent.ToString("0.0", CultureInfo.InvariantCulture)}%");
        }
        return builder.ToString().Trim();
    }

    private static PluginCommandResult SaveIngredient(PluginState state, PluginCommand command)
    {
        var id = command.Argument("id")?.Trim();
        var name = command.Argument("name")?.Trim() ?? "";
        var unit = command.Argument("unit")?.Trim().ToLowerInvariant() ?? "";
        if (name.Length is < 1 or > 80)
            return PluginCommandResult.Invalid(state, "Informe um nome de ingrediente válido.");
        if (!TryDecimal(command.Argument("price"), out var price) || price < 0)
            return PluginCommandResult.Invalid(state, "Informe um preço válido.");
        if (!TryDecimal(command.Argument("quantity"), out var quantity) || quantity <= 0)
            return PluginCommandResult.Invalid(state, "Informe uma quantidade maior que zero.");
        if (!PurchaseUnits.Contains(unit))
            return PluginCommandResult.Invalid(state, "Selecione uma unidade válida.");
        if (!TryOptionalDecimal(command.Argument("gramsPerMl"), out var density) || density < 0)
            return PluginCommandResult.Invalid(state, "A densidade precisa ser um número positivo.");

        var ingredients = FoodCostData.Ingredients(state).ToList();
        if (string.IsNullOrWhiteSpace(id))
        {
            id = Guid.NewGuid().ToString("N");
            ingredients.Add(new(id, name, price, quantity, unit, density));
        }
        else
        {
            var index = ingredients.FindIndex(item => item.Id == id);
            if (index < 0) return PluginCommandResult.Invalid(state, "Ingrediente não encontrado.");
            ingredients[index] = new(id, name, price, quantity, unit, density);
        }

        return new PluginCommandResult(state.With("ingredients", FoodCostData.SerializeIngredients(ingredients)));
    }

    private static PluginCommandResult DeleteIngredient(PluginState state, string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return PluginCommandResult.Invalid(state, "Ingrediente inválido.");

        var ingredients = FoodCostData.Ingredients(state).Where(item => item.Id != id).ToList();
        var dishes = FoodCostData.Dishes(state)
            .Select(dish => dish with { Items = dish.Items.Where(item => item.IngredientId != id).ToList() })
            .ToList();

        return new PluginCommandResult(
            state.With("ingredients", FoodCostData.SerializeIngredients(ingredients))
                 .With("dishes", FoodCostData.SerializeDishes(dishes)));
    }

    private static PluginCommandResult SaveDish(PluginState state, PluginCommand command)
    {
        var id = command.Argument("id")?.Trim();
        var name = command.Argument("name")?.Trim() ?? "";
        if (name.Length is < 1 or > 80)
            return PluginCommandResult.Invalid(state, "Informe um nome de prato válido.");
        if (!TryNonNegative(command.Argument("salePrice"), out var salePrice)
            || !TryNonNegative(command.Argument("packagingCost"), out var packaging)
            || !TryNonNegative(command.Argument("otherCost"), out var other)
            || !TryNonNegative(command.Argument("deliveryPercent"), out var delivery)
            || delivery > 100)
            return PluginCommandResult.Invalid(state, "Revise os valores do prato.");

        var dishes = FoodCostData.Dishes(state).ToList();
        if (string.IsNullOrWhiteSpace(id))
        {
            id = Guid.NewGuid().ToString("N");
            dishes.Add(new(id, name, salePrice, packaging, other, delivery, []));
        }
        else
        {
            var index = dishes.FindIndex(item => item.Id == id);
            if (index < 0) return PluginCommandResult.Invalid(state, "Prato não encontrado.");
            var current = dishes[index];
            dishes[index] = current with
            {
                Name = name,
                SalePrice = salePrice,
                PackagingCost = packaging,
                OtherCost = other,
                DeliveryPercent = delivery
            };
        }

        return new PluginCommandResult(
            state.With("dishes", FoodCostData.SerializeDishes(dishes)),
            Data: new Dictionary<string, string> { ["id"] = id });
    }

    private static PluginCommandResult DeleteDish(PluginState state, string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return PluginCommandResult.Invalid(state, "Prato inválido.");
        var dishes = FoodCostData.Dishes(state).Where(item => item.Id != id).ToList();
        return new PluginCommandResult(state.With("dishes", FoodCostData.SerializeDishes(dishes)));
    }

    private static PluginCommandResult SaveItem(PluginState state, PluginCommand command)
    {
        var dishId = command.Argument("dishId")?.Trim() ?? "";
        var itemId = command.Argument("itemId")?.Trim();
        var ingredientId = command.Argument("ingredientId")?.Trim() ?? "";
        var unit = command.Argument("unit")?.Trim().ToLowerInvariant() ?? "";

        if (!TryDecimal(command.Argument("quantity"), out var quantity) || quantity <= 0)
            return PluginCommandResult.Invalid(state, "Informe uma quantidade maior que zero.");

        var ingredients = FoodCostData.Ingredients(state);
        var ingredient = ingredients.FirstOrDefault(item => item.Id == ingredientId);
        if (ingredient is null)
            return PluginCommandResult.Invalid(state, "Selecione um ingrediente.");
        if (!FoodCostCalculator.RecipeUnits(ingredient).Contains(unit, StringComparer.OrdinalIgnoreCase))
            return PluginCommandResult.Invalid(state, "Essa medida não é compatível com o ingrediente.");

        var dishes = FoodCostData.Dishes(state).ToList();
        var dishIndex = dishes.FindIndex(item => item.Id == dishId);
        if (dishIndex < 0)
            return PluginCommandResult.Invalid(state, "Salve o prato antes de adicionar ingredientes.");

        var items = dishes[dishIndex].Items.ToList();
        if (string.IsNullOrWhiteSpace(itemId))
            items.Add(new(Guid.NewGuid().ToString("N"), ingredientId, quantity, unit));
        else
        {
            var itemIndex = items.FindIndex(item => item.Id == itemId);
            if (itemIndex < 0) return PluginCommandResult.Invalid(state, "Item não encontrado.");
            items[itemIndex] = new(itemId, ingredientId, quantity, unit);
        }

        dishes[dishIndex] = dishes[dishIndex] with { Items = items };
        return new PluginCommandResult(state.With("dishes", FoodCostData.SerializeDishes(dishes)));
    }

    private static PluginCommandResult DeleteItem(PluginState state, string? dishId, string? itemId)
    {
        if (string.IsNullOrWhiteSpace(dishId) || string.IsNullOrWhiteSpace(itemId))
            return PluginCommandResult.Invalid(state, "Item inválido.");

        var dishes = FoodCostData.Dishes(state).ToList();
        var dishIndex = dishes.FindIndex(item => item.Id == dishId);
        if (dishIndex < 0) return PluginCommandResult.Invalid(state, "Prato não encontrado.");

        dishes[dishIndex] = dishes[dishIndex] with
        {
            Items = dishes[dishIndex].Items.Where(item => item.Id != itemId).ToList()
        };
        return new PluginCommandResult(state.With("dishes", FoodCostData.SerializeDishes(dishes)));
    }

    private static List<FoodIngredient> NormalizeIngredients(IEnumerable<FoodIngredient> source)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<FoodIngredient>();
        foreach (var item in source)
        {
            var id = string.IsNullOrWhiteSpace(item.Id) || !seen.Add(item.Id)
                ? Guid.NewGuid().ToString("N")
                : item.Id;
            seen.Add(id);
            var unit = PurchaseUnits.Contains(item.PurchaseUnit ?? "") ? item.PurchaseUnit.ToLowerInvariant() : "g";
            result.Add(new(
                id,
                (item.Name ?? "").Trim(),
                Math.Max(0, item.PurchasePrice),
                item.PurchaseQuantity > 0 ? item.PurchaseQuantity : 1,
                unit,
                Math.Max(0, item.GramsPerMl)));
        }
        return result;
    }

    private static List<FoodDish> NormalizeDishes(IEnumerable<FoodDish> source, HashSet<string> ingredientIds)
    {
        var seenDishes = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<FoodDish>();
        foreach (var dish in source)
        {
            var id = string.IsNullOrWhiteSpace(dish.Id) || !seenDishes.Add(dish.Id)
                ? Guid.NewGuid().ToString("N")
                : dish.Id;
            seenDishes.Add(id);

            var itemIds = new HashSet<string>(StringComparer.Ordinal);
            var items = (dish.Items ?? [])
                .Where(item => ingredientIds.Contains(item.IngredientId))
                .Select(item =>
                {
                    var itemId = string.IsNullOrWhiteSpace(item.Id) || !itemIds.Add(item.Id)
                        ? Guid.NewGuid().ToString("N")
                        : item.Id;
                    itemIds.Add(itemId);
                    return new FoodRecipeItem(
                        itemId,
                        item.IngredientId,
                        Math.Max(0, item.Quantity),
                        item.Unit ?? "");
                }).ToList();

            result.Add(new(
                id,
                (dish.Name ?? "").Trim(),
                Math.Max(0, dish.SalePrice),
                Math.Max(0, dish.PackagingCost),
                Math.Max(0, dish.OtherCost),
                Math.Min(100, Math.Max(0, dish.DeliveryPercent)),
                items));
        }
        return result;
    }

    private static bool TryNonNegative(string? raw, out decimal value) =>
        TryDecimal(raw, out value) && value >= 0;

    private static bool TryOptionalDecimal(string? raw, out decimal value)
    {
        if (string.IsNullOrWhiteSpace(raw)) { value = 0; return true; }
        return TryDecimal(raw, out value);
    }

    private static bool TryDecimal(string? raw, out decimal value) =>
        decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
}
