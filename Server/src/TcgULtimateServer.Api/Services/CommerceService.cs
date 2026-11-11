using TcgULtimateServer.Api.Models;

namespace TcgULtimateServer.Api.Services;

public record PurchaseRequest(string ProductId);
public record PurchaseReceipt(string ProductId, string Currency, long Balance, List<CardInstance> Cards, string? AssetId);

/// <summary>
/// Server-authoritative soft-currency purchases. Prevents duplicate concurrent
/// deductions, validates inventory grants, and records all currency movement.
/// No external payment processing is performed here.
/// </summary>
public sealed class CommerceService(AppState state)
{
    private readonly object gate = new();

    public PurchaseReceipt Purchase(string userId, string productId)
    {
        lock (gate)
        {
            if (!state.StoreProducts.TryGetValue(productId, out var product) || !product.Active)
                throw new InvalidOperationException("Product is not available.");
            if (product.Price < 0 || decimal.Truncate(product.Price) != product.Price ||
                product.Price > long.MaxValue)
                throw new InvalidOperationException("Product price must be a valid whole-credit amount.");
            if (!state.Wallets.TryGetValue(userId, out var wallet) ||
                !state.Inventories.TryGetValue(userId, out var inventory))
                throw new InvalidOperationException("The user has no inventory or wallet.");
            var cost = checked((long)product.Price);
            var balance = wallet.Balances.GetValueOrDefault(product.Currency);
            if (balance < cost) throw new InvalidOperationException("Insufficient " + product.Currency + ".");

            var grants = new List<CardInstance>();
            if (!string.IsNullOrWhiteSpace(product.ContentPackId))
            {
                var pack = state.GamePackages.Values
                    .Where(record => record.Manifest.PackageId == product.ContentPackId)
                    .OrderByDescending(record => record.ImportedAt).FirstOrDefault()?.RulePack;
                if (pack == null) state.RulePacks.TryGetValue(product.ContentPackId, out pack);
                if (pack == null) throw new InvalidOperationException("Product content pack is unavailable.");

                var options = pack.StartingDeck.Count > 0 ? pack.StartingDeck :
                    pack.Cards.Select(card => card.Id).ToList();
                if (options.Count == 0) throw new InvalidOperationException("Product has no cards to grant.");
                for (var index = 0; index < 5; index++)
                {
                    var card = options[System.Security.Cryptography.RandomNumberGenerator.GetInt32(options.Count)];
                    grants.Add(new CardInstance(Guid.NewGuid().ToString("N"), card, userId));
                }
            }
            if (grants.Count == 0 && string.IsNullOrWhiteSpace(product.AssetId))
                throw new InvalidOperationException("Product has no deliverable content.");

            // Deductions and grants are performed under the same process-wide lock.
            wallet.Balances[product.Currency] = balance - cost;
            inventory.Cards.AddRange(grants);
            if (!string.IsNullOrWhiteSpace(product.AssetId))
                inventory.Achievements.Add("entitlement:" + product.AssetId);
            var transaction = new CurrencyTransaction(Guid.NewGuid().ToString("N"), userId,
                product.Currency, -cost, wallet.Balances[product.Currency],
                "store:" + product.Id, userId, DateTimeOffset.UtcNow);
            state.CurrencyTransactions[transaction.Id] = transaction;
            return new PurchaseReceipt(product.Id, product.Currency,
                wallet.Balances[product.Currency], grants, product.AssetId);
        }
    }
}
