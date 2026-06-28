using Npgsql;

namespace MultiModelVisualizer.Api.Data;

public class DatabaseInitializer
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(string connectionString, ILogger<DatabaseInitializer> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing database schema...");

        const string sql = """
            CREATE TABLE IF NOT EXISTS learning_sessions (
              session_id    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
              user_id       UUID NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001',
              current_state VARCHAR(50) NOT NULL DEFAULT 'Created',
              topic         TEXT,
              intent        TEXT,
              domain        TEXT,
              selected_components TEXT,
              difficulty_level VARCHAR(20),
              visualization_type VARCHAR(50),
              visualization_plan JSONB,
              explanation   TEXT,
              final_output  TEXT,
              fast_track    BOOLEAN NOT NULL DEFAULT FALSE,
              created_date  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
              updated_date  TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            ALTER TABLE learning_sessions ADD COLUMN IF NOT EXISTS fast_track BOOLEAN NOT NULL DEFAULT FALSE;
            ALTER TABLE learning_sessions ADD COLUMN IF NOT EXISTS component_source_strategy VARCHAR(50) NOT NULL DEFAULT 'ai_generated';
            ALTER TABLE learning_sessions ADD COLUMN IF NOT EXISTS citations JSONB;
            ALTER TABLE learning_sessions ADD COLUMN IF NOT EXISTS cloned_from_session_id UUID;
            ALTER TABLE learning_sessions ADD COLUMN IF NOT EXISTS expires_at TIMESTAMPTZ;
            ALTER TABLE learning_sessions ADD COLUMN IF NOT EXISTS cancelled_at TIMESTAMPTZ;

            ALTER TABLE generation_jobs ADD COLUMN IF NOT EXISTS retry_count INT NOT NULL DEFAULT 0;
            ALTER TABLE generation_jobs ADD COLUMN IF NOT EXISTS next_retry_at TIMESTAMPTZ;
            ALTER TABLE generation_jobs ADD COLUMN IF NOT EXISTS review_status VARCHAR(50);
            ALTER TABLE generation_jobs ADD COLUMN IF NOT EXISTS review_severity VARCHAR(50);
            ALTER TABLE generation_jobs ADD COLUMN IF NOT EXISTS review_notes TEXT;
            ALTER TABLE generation_jobs ADD COLUMN IF NOT EXISTS queue_name VARCHAR(50) NOT NULL DEFAULT 'generation.diagram';
            ALTER TABLE generation_jobs ADD COLUMN IF NOT EXISTS output_content TEXT;

            CREATE TABLE IF NOT EXISTS learning_session_events (
              event_id       UUID PRIMARY KEY DEFAULT gen_random_uuid(),
              session_id     UUID NOT NULL REFERENCES learning_sessions(session_id),
              previous_state VARCHAR(50),
              new_state      VARCHAR(50) NOT NULL,
              trigger        VARCHAR(100),
              event_payload  JSONB,
              created_at     TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS generation_jobs (
              job_id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
              session_id         UUID NOT NULL REFERENCES learning_sessions(session_id),
              status             VARCHAR(50) NOT NULL DEFAULT 'Queued',
              visualization_type VARCHAR(50),
              fallback_attempt   INT NOT NULL DEFAULT 0,
              output_type        VARCHAR(50),
              output_url         TEXT,
              output_content     TEXT,
              thumbnail_url      TEXT,
              error_code         VARCHAR(100),
              progress           INT NOT NULL DEFAULT 0,
              created_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
              updated_at         TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();

        _logger.LogInformation("Database schema initialized successfully.");
    }
}
