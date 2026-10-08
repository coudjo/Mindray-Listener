using Microsoft.EntityFrameworkCore;

namespace MindrayMiddleware;

/// <summary>
/// Maps the result tables owned by the izi-labs web app.
/// Column names match LabDbContext. This context does not run migrations.
/// </summary>
public class ListenerDbContext : DbContext
{
    public ListenerDbContext(DbContextOptions<ListenerDbContext> options) : base(options)
    {
    }

    public DbSet<TestRequest> TestRequests => Set<TestRequest>();
    public DbSet<TestResult> TestResults => Set<TestResult>();
    public DbSet<ResultParameter> ResultParameters => Set<ResultParameter>();
    public DbSet<ResultHistogram> ResultHistograms => Set<ResultHistogram>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestRequest>(entity =>
        {
            entity.ToTable("TestRequests");
            entity.HasKey(x => x.TestRequestId);
            entity.Property(x => x.SampleId).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(16).IsRequired();
        });

        modelBuilder.Entity<TestResult>(entity =>
        {
            entity.ToTable("TestResults");
            entity.HasKey(x => x.TestResultId);
            entity.Property(x => x.SampleIdReceived).HasMaxLength(32).IsRequired();
            entity.HasIndex(x => x.TestRequestId).IsUnique();
            entity.HasMany(x => x.Parameters).WithOne().HasForeignKey(x => x.TestResultId);
            entity.HasMany(x => x.Histograms).WithOne().HasForeignKey(x => x.TestResultId);
        });

        modelBuilder.Entity<ResultParameter>(entity =>
        {
            entity.ToTable("ResultParameters");
            entity.HasKey(x => x.ResultParameterId);
            entity.Property(x => x.ParamName).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Value).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Low).HasMaxLength(32).IsRequired();
            entity.Property(x => x.High).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Unit).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Flag).HasMaxLength(2);
        });

        modelBuilder.Entity<ResultHistogram>(entity =>
        {
            entity.ToTable("ResultHistograms");
            entity.HasKey(x => x.ResultHistogramId);
            entity.Property(x => x.HistoType).HasMaxLength(16).IsRequired();
        });
    }
}

public class TestRequest
{
    public int TestRequestId { get; set; }
    public string SampleId { get; set; } = "";
    public string Status { get; set; } = "Pending";
}

public class TestResult
{
    public int TestResultId { get; set; }
    public int? TestRequestId { get; set; }
    public string SampleIdReceived { get; set; } = "";
    public DateTime? DeviceTestTime { get; set; }
    public DateTime ReceivedAt { get; set; }
    public byte[] RawPayload { get; set; } = [];
    public List<ResultParameter> Parameters { get; set; } = [];
    public List<ResultHistogram> Histograms { get; set; } = [];
}

public class ResultParameter
{
    public int ResultParameterId { get; set; }
    public int TestResultId { get; set; }
    public string ParamName { get; set; } = "";
    public string Value { get; set; } = "";
    public string Low { get; set; } = "";
    public string High { get; set; } = "";
    public string Unit { get; set; } = "";
    public string? Flag { get; set; }
    public int SortOrder { get; set; }
}

public class ResultHistogram
{
    public int ResultHistogramId { get; set; }
    public int TestResultId { get; set; }
    public string HistoType { get; set; } = "";
    public byte[] RawBytes { get; set; } = [];
}
