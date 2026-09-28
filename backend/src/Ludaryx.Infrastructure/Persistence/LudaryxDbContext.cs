using Microsoft.EntityFrameworkCore;

namespace Ludaryx.Infrastructure.Persistence;

public class LudaryxDbContext(DbContextOptions<LudaryxDbContext> options)
    : DbContext(options);
