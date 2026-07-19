-- Provider rows are installation-specific and are now created by the first-run wizard.
-- This migration intentionally remains as a no-op so already-deployed DbUp journals and
-- migration ordering stay stable; no production reference data is seeded automatically.

SELECT 1 WHERE 1 = 0;
GO
