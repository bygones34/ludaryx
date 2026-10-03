using System.Data;
using System.Security.Cryptography;
using System.Text;
using Ludaryx.Application.Auth;
using Ludaryx.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Ludaryx.Infrastructure.Authentication;

public sealed class EfRefreshSessionStore(LudaryxDbContext db) : IRefreshSessionStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public async Task<RefreshSession> CreateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var familyId = Guid.NewGuid();
        var (token, record) = NewToken(userId, familyId, now);
        record.Id = familyId;
        db.RefreshTokens.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        return new RefreshSession(token, record.ExpiresAt);
    }

    public async Task<RotatedRefreshSession?> RotateAsync(
        string token, CancellationToken cancellationToken)
    {
        if (!CanLookUp(token))
        {
            return null;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var record = await LockTokenAsync(token, cancellationToken);
        if (record is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        if (record.ReplacedByTokenId is not null)
        {
            await RevokeActiveFamilyAsync(record.FamilyId, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        if (record.RevokedAt is not null || record.ExpiresAt <= now)
        {
            return null;
        }

        var (replacementToken, replacement) = NewToken(record.UserId, record.FamilyId, now);
        record.RevokedAt = now;
        record.ReplacedByTokenId = replacement.Id;
        db.RefreshTokens.Add(replacement);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new RotatedRefreshSession(record.UserId,
            new RefreshSession(replacementToken, replacement.ExpiresAt));
    }

    public async Task RevokeFamilyAsync(string? token, CancellationToken cancellationToken)
    {
        if (!CanLookUp(token))
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var record = await LockTokenAsync(token!, cancellationToken);
        if (record is null)
        {
            return;
        }

        await RevokeActiveFamilyAsync(record.FamilyId, DateTimeOffset.UtcNow, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<RefreshTokenRecord?> LockTokenAsync(
        string token, CancellationToken cancellationToken)
    {
        var hash = Hash(token);
        var candidate = await db.RefreshTokens.AsNoTracking()
            .SingleOrDefaultAsync(record => record.TokenHash == hash, cancellationToken);
        if (candidate is null)
        {
            return null;
        }

        // Every operation in a session family locks its root record first.
        // This serializes rotation and revocation even when they present different tokens.
        var roots = await db.RefreshTokens
            .FromSqlInterpolated($"SELECT * FROM \"RefreshTokens\" WHERE \"Id\" = {candidate.FamilyId} FOR UPDATE")
            .ToListAsync(cancellationToken);
        if (roots.Count == 0)
        {
            return null;
        }

        return await db.RefreshTokens.FindAsync([candidate.Id], cancellationToken);
    }

    private Task<int> RevokeActiveFamilyAsync(
        Guid familyId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.RefreshTokens
            .Where(record => record.FamilyId == familyId && record.RevokedAt == null)
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(record => record.RevokedAt, now),
                cancellationToken);

    private static (string Token, RefreshTokenRecord Record) NewToken(
        Guid userId, Guid familyId, DateTimeOffset now)
    {
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return (token, new RefreshTokenRecord
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FamilyId = familyId,
            TokenHash = Hash(token),
            CreatedAt = now,
            ExpiresAt = now.Add(Lifetime)
        });
    }

    private static bool CanLookUp(string? token) =>
        token is { Length: 43 } && token.All(c =>
            char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static byte[] Hash(string token) =>
        SHA256.HashData(Encoding.ASCII.GetBytes(token));
}
