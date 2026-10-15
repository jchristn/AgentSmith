namespace Test.Shared.Connectors.Sql
{
    using System.Collections.Generic;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Sql;

    /// <summary>
    /// The statement classifier table: reads, writes, destructive statements, comment and literal tricks, CTEs,
    /// EXPLAIN ANALYZE, implicit T-SQL boundaries, dynamic SQL, multi-statement handling, and unparseable input across
    /// all four dialects.
    /// </summary>
    public static class ClassifierCases
    {
        #region Private-Members

        private const SqlEngineEnum P = SqlEngineEnum.Postgresql;
        private const SqlEngineEnum M = SqlEngineEnum.Mysql;
        private const SqlEngineEnum S = SqlEngineEnum.SqlServer;
        private const SqlEngineEnum L = SqlEngineEnum.Sqlite;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the table.
        /// </summary>
        /// <returns>Cases.</returns>
        public static List<ClassifierCase> Build()
        {
            List<ClassifierCase> c = new List<ClassifierCase>();

            // PostgreSQL reads, including comment, literal, and quoting tricks.
            R(c, P, "SELECT 1");
            R(c, P, "select * from customers where customer_id = 1");
            R(c, P, "  SELECT 1;");
            R(c, P, "SELECT 1;;");
            R(c, P, "(SELECT 1) UNION (SELECT 2)");
            R(c, P, "SELECT 'DROP TABLE x; DELETE FROM y'");
            R(c, P, "SELECT 1 -- ; DROP TABLE x");
            R(c, P, "SELECT 1 /* ; DELETE FROM t */");
            R(c, P, "/* leading */ SELECT 1");
            R(c, P, "-- comment\nSELECT 1");
            R(c, P, "SELECT $$; DROP TABLE t; $$");
            R(c, P, "SELECT $tag$ ; DELETE FROM t $tag$ AS x");
            R(c, P, "SELECT E'it\\'s; DROP TABLE t'");
            R(c, P, "SELECT '\\''; DROP TABLE t; -- '");
            R(c, P, "SELECT \"delete\" FROM \"drop\"");
            R(c, P, "SELECT /* nested /* inner */ ; DROP TABLE t */ 1");
            R(c, P, "WITH x AS (SELECT 1) SELECT * FROM x");
            R(c, P, "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 10) SELECT i FROM n");
            R(c, P, "WITH a AS (SELECT 1), b AS (SELECT 2) SELECT * FROM a, b");
            R(c, P, "WITH a AS MATERIALIZED (SELECT 1) SELECT * FROM a");
            R(c, P, "WITH a AS NOT MATERIALIZED (SELECT 1) SELECT * FROM a");
            R(c, P, "SHOW search_path");
            R(c, P, "SHOW ALL");
            R(c, P, "EXPLAIN SELECT * FROM orders");
            R(c, P, "EXPLAIN (FORMAT JSON) SELECT 1");
            R(c, P, "EXPLAIN DELETE FROM orders");
            R(c, P, "EXPLAIN ANALYZE SELECT * FROM orders");
            R(c, P, "EXPLAIN (ANALYZE, BUFFERS) SELECT 1");
            R(c, P, "VALUES (1, 'a'), (2, 'b')");
            R(c, P, "TABLE customers");
            R(c, P, "SELECT count(*) FROM orders o JOIN customers c ON c.customer_id = o.customer_id GROUP BY c.country");
            R(c, P, "SELECT x::int FROM t");
            R(c, P, "SELECT * FROM t WHERE name ILIKE '%update%'");
            R(c, P, "SELECT REPLACE(name, 'a', 'b') FROM t");
            R(c, P, "SELECT now() - interval '1 hour'");
            R(c, P, "SELECT * FROM orders WHERE customer_id = @id");
            R(c, P, "SELECT * FROM orders WHERE customer_id = $1");
            R(c, P, "SELECT pg_size_pretty(pg_database_size(current_database()))");
            R(c, P, "select coalesce(sum(total), 0) from orders");
            R(c, P, "SELECT * FROM t ORDER BY 1 FETCH FIRST 5 ROWS ONLY");
            R(c, P, "SELECT 1 AS \"x;y\"");

            // MySQL reads.
            R(c, M, "SELECT 1");
            R(c, M, "SELECT * FROM `orders` WHERE `status` = 'shipped'");
            R(c, M, "SHOW TABLES");
            R(c, M, "SHOW FULL PROCESSLIST");
            R(c, M, "SHOW CREATE TABLE orders");
            R(c, M, "DESCRIBE orders");
            R(c, M, "DESC orders");
            R(c, M, "EXPLAIN SELECT * FROM orders");
            R(c, M, "EXPLAIN FORMAT=JSON SELECT * FROM orders");
            R(c, M, "EXPLAIN ANALYZE SELECT * FROM orders");
            R(c, M, "EXPLAIN orders");
            R(c, M, "SELECT 'a\\'; DROP TABLE t; -- '");
            R(c, M, "SELECT \"x; DELETE FROM t\"");
            R(c, M, "SELECT 1 # ; DROP TABLE t");
            R(c, M, "SELECT 1 -- ; DROP TABLE t");
            R(c, M, "SELECT IF(1 > 0, 'yes', 'no')");
            R(c, M, "SELECT INSERT('abc', 1, 1, 'x')");
            R(c, M, "SELECT REPLACE('abc', 'a', 'b')");
            R(c, M, "SELECT * FROM orders USE INDEX (idx_orders_customer_id) WHERE customer_id = 1");
            R(c, M, "SELECT CONVERT(full_name USING utf8mb4) FROM customers");
            R(c, M, "WITH t AS (SELECT 1 AS x) SELECT x FROM t");
            R(c, M, "SELECT * FROM t WHERE a = ? LIMIT 10");
            R(c, M, "SELECT `delete` FROM `drop`");
            R(c, M, "SHOW VARIABLES LIKE 'max_connections'");
            R(c, M, "SELECT @@version");
            R(c, M, "SELECT SLEEP(1)");

            // SQL Server reads.
            R(c, S, "SELECT TOP (10) * FROM dbo.orders");
            R(c, S, "SELECT [delete] FROM [drop]");
            R(c, S, "SELECT N'x; DROP TABLE t'");
            R(c, S, "SELECT 1 /* outer /* inner */ ; DROP TABLE t */");
            R(c, S, "WITH cte AS (SELECT 1 AS x) SELECT x FROM cte");
            R(c, S, ";WITH cte AS (SELECT 1 AS x) SELECT x FROM cte");
            R(c, S, "SELECT @@VERSION");
            R(c, S, "SELECT * FROM sys.dm_exec_requests");
            R(c, S, "WAITFOR DELAY '00:00:01'");
            R(c, S, "SELECT o.order_id FROM orders o ORDER BY o.order_id OFFSET 0 ROWS FETCH NEXT 5 ROWS ONLY");
            R(c, S, "SELECT * FROM #temp");
            R(c, S, "SELECT CAST(1 AS INT) AS [x]] y]");
            R(c, S, "SELECT IIF(1 = 1, 'a', 'b')");
            R(c, S, "SELECT * FROM orders WHERE customer_id = @id");

            // SQLite reads.
            R(c, L, "SELECT * FROM customers");
            R(c, L, "PRAGMA table_info(customers)");
            R(c, L, "PRAGMA table_info('customers')");
            R(c, L, "PRAGMA main.table_info(customers)");
            R(c, L, "PRAGMA integrity_check");
            R(c, L, "PRAGMA user_version");
            R(c, L, "PRAGMA foreign_key_list(orders)");
            R(c, L, "EXPLAIN QUERY PLAN SELECT * FROM orders WHERE customer_id = 1");
            R(c, L, "EXPLAIN SELECT 1");
            R(c, L, "SELECT [order] FROM `t`");
            R(c, L, "WITH x AS (SELECT 1) SELECT * FROM x");
            R(c, L, "VALUES (1)");
            R(c, L, "SELECT * FROM t WHERE name = :name");
            R(c, L, "PRAGMA index_list(orders)");
            R(c, L, "SELECT date('now', '-1 day')");

            // PostgreSQL writes.
            W(c, P, "INSERT INTO t (a) VALUES (1)");
            W(c, P, "UPDATE t SET a = 1 WHERE id = 2");
            W(c, P, "UPDATE t SET a = 1");
            W(c, P, "MERGE INTO t USING s ON t.id = s.id WHEN MATCHED THEN UPDATE SET a = s.a");
            W(c, P, "CREATE TABLE t (id int)");
            W(c, P, "CREATE INDEX i ON t (a)");
            W(c, P, "CREATE TABLE t2 AS SELECT * FROM t");
            W(c, P, "GRANT SELECT ON t TO bob");
            W(c, P, "GRANT SELECT, DELETE ON t TO bob");
            W(c, P, "CALL do_thing()");
            W(c, P, "SET search_path TO public");
            W(c, P, "VACUUM");
            W(c, P, "ANALYZE orders");
            W(c, P, "COPY t TO STDOUT");
            W(c, P, "SELECT * INTO new_t FROM t");
            W(c, P, "SELECT * FROM t FOR UPDATE");
            W(c, P, "SELECT * FROM t FOR SHARE");
            W(c, P, "SELECT nextval('order_number_seq')");
            W(c, P, "SELECT setval('s', 10)");
            W(c, P, "SELECT set_config('work_mem', '64MB', false)");
            W(c, P, "SELECT pg_advisory_lock(1)");
            W(c, P, "WITH x AS (INSERT INTO t VALUES (1) RETURNING *) SELECT * FROM x");
            W(c, P, "WITH x AS (UPDATE t SET a = 1 RETURNING *) SELECT * FROM x");
            W(c, P, "INSERT INTO t SELECT * FROM s");
            W(c, P, "INSERT INTO t VALUES (1) ON CONFLICT (id) DO UPDATE SET a = 2");
            W(c, P, "BEGIN");
            W(c, P, "COMMIT");
            W(c, P, "ROLLBACK");
            W(c, P, "START TRANSACTION READ WRITE");
            W(c, P, "REFRESH MATERIALIZED VIEW mv");
            W(c, P, "REINDEX TABLE t");
            W(c, P, "COMMENT ON TABLE t IS 'x'");
            W(c, P, "LOCK TABLE t IN ACCESS EXCLUSIVE MODE");
            W(c, P, "EXPLAIN ANALYZE INSERT INTO t VALUES (1)");
            W(c, P, "EXPLAIN (ANALYZE true) UPDATE t SET a = 1");
            W(c, P, "EXPLAIN ANALYSE INSERT INTO t VALUES (1)");
            W(c, P, "DO $$ BEGIN INSERT INTO t VALUES (1); END $$");
            W(c, P, "NOTIFY channel");
            W(c, P, "UPSERT INTO t VALUES (1)");
            W(c, P, "PREPARE p AS SELECT 1");
            W(c, P, "EXECUTE p");

            // MySQL writes.
            W(c, M, "INSERT INTO orders (order_id) VALUES (99)");
            W(c, M, "REPLACE INTO t VALUES (1)");
            W(c, M, "INSERT INTO t VALUES (1) ON DUPLICATE KEY UPDATE a = 2");
            W(c, M, "UPDATE t SET a = 1 WHERE id = 1");
            W(c, M, "CREATE TABLE t (id INT)");
            W(c, M, "CALL refresh_stats()");
            W(c, M, "SET @x = 1");
            W(c, M, "SET SESSION sql_mode = ''");
            W(c, M, "ANALYZE TABLE orders");
            W(c, M, "OPTIMIZE TABLE orders");
            W(c, M, "SELECT * FROM t INTO OUTFILE '/tmp/x'");
            W(c, M, "SELECT a INTO @v FROM t LIMIT 1");
            W(c, M, "SELECT * FROM t LOCK IN SHARE MODE");
            W(c, M, "SELECT * FROM t FOR UPDATE");
            W(c, M, "LOAD DATA INFILE '/tmp/x' INTO TABLE t");
            W(c, M, "SELECT GET_LOCK('x', 1)");
            W(c, M, "/*!40101 SET NAMES utf8 */");
            W(c, M, "USE shop");
            W(c, M, "START TRANSACTION");
            W(c, M, "PREPARE s FROM 'SELECT 1'");
            W(c, M, "EXPLAIN ANALYZE UPDATE t SET a = 1");
            W(c, M, "FLUSH TABLES");
            W(c, M, "GRANT SELECT ON shop.* TO 'bob'@'%'");
            Batch(c, M, "SELECT 1 /*! ; INSERT INTO t VALUES (1) */", EffectEnum.Write);

            // SQL Server writes, including statements hidden without a separator.
            W(c, S, "INSERT INTO dbo.orders (order_id) VALUES (99)");
            W(c, S, "UPDATE TOP (10) dbo.t SET a = 1");
            W(c, S, "MERGE dbo.t AS tgt USING dbo.s AS src ON tgt.id = src.id WHEN MATCHED THEN UPDATE SET a = src.a WHEN NOT MATCHED THEN INSERT (id) VALUES (src.id);");
            W(c, S, "EXEC sp_who2");
            W(c, S, "EXECUTE dbo.usp_refresh @id = 1");
            W(c, S, "SET NOCOUNT ON");
            W(c, S, "SELECT * INTO #tmp FROM dbo.orders");
            W(c, S, "CREATE PROCEDURE p AS SELECT 1");
            W(c, S, "DBCC FREEPROCCACHE");
            W(c, S, "BEGIN TRAN");
            W(c, S, "DECLARE @x INT");
            W(c, S, "SELECT 1 INSERT INTO t VALUES (1)");
            W(c, S, "SELECT 1 EXEC sp_who");
            W(c, S, "BACKUP DATABASE shop TO DISK = '/tmp/x.bak'");
            W(c, S, "EXEC('SELECT 1')");
            W(c, S, "IF 1 = 1 SELECT 1");

            // SQLite writes.
            W(c, L, "INSERT INTO t VALUES (1)");
            W(c, L, "INSERT OR REPLACE INTO t VALUES (1)");
            W(c, L, "REPLACE INTO t VALUES (1)");
            W(c, L, "UPDATE t SET a = 1");
            W(c, L, "CREATE TABLE t (id INTEGER)");
            W(c, L, "PRAGMA journal_mode = WAL");
            W(c, L, "PRAGMA foreign_keys = OFF");
            W(c, L, "PRAGMA foreign_keys(0)");
            W(c, L, "PRAGMA optimize");
            W(c, L, "PRAGMA wal_checkpoint");
            W(c, L, "VACUUM");
            W(c, L, "ANALYZE");
            W(c, L, "ATTACH DATABASE '/tmp/x.db' AS x");
            W(c, L, "DETACH DATABASE x");
            W(c, L, "INSERT INTO t VALUES (1) ON CONFLICT(id) DO UPDATE SET a = 2");

            // PostgreSQL destructive.
            D(c, P, "DELETE FROM t WHERE id = 1");
            D(c, P, "DELETE FROM t");
            D(c, P, "DROP TABLE t");
            D(c, P, "DROP TABLE IF EXISTS t CASCADE");
            D(c, P, "TRUNCATE t");
            D(c, P, "TRUNCATE TABLE t RESTART IDENTITY");
            D(c, P, "ALTER TABLE t ADD COLUMN x int");
            D(c, P, "REVOKE SELECT ON t FROM bob");
            D(c, P, "WITH d AS (DELETE FROM t RETURNING *) SELECT * FROM d");
            D(c, P, "WITH a AS (SELECT 1), d AS (DELETE FROM t RETURNING *) SELECT * FROM a");
            D(c, P, "EXPLAIN ANALYZE DELETE FROM t");
            D(c, P, "EXPLAIN (ANALYZE, VERBOSE) DELETE FROM t");
            D(c, P, "SELECT pg_terminate_backend(123)");
            D(c, P, "SELECT pg_cancel_backend(123)");
            D(c, P, "DO $$ BEGIN DELETE FROM t; END $$");
            D(c, P, "DROP DATABASE shop");
            D(c, P, "ALTER ROLE bob SUPERUSER");
            D(c, P, "WITH x AS (SELECT 1) DELETE FROM t WHERE id IN (SELECT * FROM x)");
            D(c, P, "MERGE INTO t USING s ON t.id = s.id WHEN MATCHED THEN DELETE");
            D(c, P, "INSERT INTO log SELECT * FROM (DELETE FROM t RETURNING *) d");
            D(c, P, "SELECT * FROM t WHERE id IN (DELETE FROM x RETURNING id)");
            D(c, L, "SELECT load_extension('evil')");

            // MySQL destructive, including the backslash and "--" rules.
            D(c, M, "DELETE FROM orders WHERE order_id = 1");
            D(c, M, "DROP TABLE orders");
            D(c, M, "TRUNCATE TABLE orders");
            D(c, M, "ALTER TABLE orders ADD COLUMN x INT");
            D(c, M, "RENAME TABLE a TO b");
            D(c, M, "KILL 42");
            D(c, M, "KILL QUERY 42");
            D(c, M, "REVOKE ALL ON shop.* FROM 'bob'@'%'");
            D(c, M, "DELETE t1 FROM t1 JOIN t2 ON t1.id = t2.id");
            D(c, M, "WITH x AS (SELECT 1) DELETE FROM t WHERE id IN (SELECT * FROM x)");
            D(c, M, "DROP DATABASE shop");
            D(c, M, "SHUTDOWN");
            X(c, M, "SELECT 1--1; DROP TABLE t");
            Batch(c, M, "SELECT 1--1; DROP TABLE t", EffectEnum.Destructive);
            X(c, M, "SELECT '\\''; DROP TABLE t; -- '");
            Batch(c, M, "SELECT '\\''; DROP TABLE t; -- '", EffectEnum.Destructive);
            X(c, M, "SELECT 1 /*! ; INSERT INTO t VALUES (1) */");

            // SQL Server destructive, including implicit statement boundaries and dynamic SQL.
            D(c, S, "DELETE FROM dbo.orders WHERE order_id = 1");
            D(c, S, "DROP TABLE dbo.orders");
            D(c, S, "TRUNCATE TABLE dbo.orders");
            D(c, S, "ALTER TABLE dbo.orders ADD x INT");
            D(c, S, "SELECT 1 DELETE FROM dbo.orders");
            D(c, S, "SELECT * FROM dbo.orders DROP TABLE dbo.orders");
            D(c, S, "WITH cte AS (SELECT TOP (5) * FROM dbo.orders) DELETE FROM cte");
            D(c, S, "MERGE dbo.t AS tgt USING dbo.s AS src ON tgt.id = src.id WHEN NOT MATCHED BY SOURCE THEN DELETE;");
            D(c, S, "EXEC xp_cmdshell 'dir'");
            D(c, S, "EXEC master..xp_cmdshell 'dir'");
            D(c, S, "EXEC('DROP TABLE dbo.t')");
            D(c, S, "EXEC sp_executesql N'DELETE FROM dbo.t'");
            D(c, S, "IF EXISTS (SELECT 1 FROM dbo.t) DROP TABLE dbo.t");
            D(c, S, "DENY SELECT ON dbo.t TO bob");
            D(c, S, "KILL 53");
            D(c, S, "INSERT INTO dbo.t VALUES (1) DELETE FROM dbo.t");
            D(c, S, "UPDATE dbo.t SET a = 1 DROP TABLE dbo.t");
            D(c, S, "RESTORE DATABASE shop FROM DISK = '/tmp/x.bak'");
            X(c, S, "SELECT [x] FROM t; DROP TABLE t");

            // SQLite destructive.
            D(c, L, "DELETE FROM customers");
            D(c, L, "DROP TABLE customers");
            D(c, L, "DROP VIEW customer_order_totals");
            D(c, L, "ALTER TABLE customers ADD COLUMN x TEXT");
            D(c, L, "ALTER TABLE customers RENAME TO c2");
            D(c, L, "WITH x AS (SELECT 1) DELETE FROM t");

            // Multiple statements: rejected unless allowed, then the most severe.
            X(c, P, "SELECT 1; SELECT 2");
            Batch(c, P, "SELECT 1; SELECT 2", EffectEnum.Read);
            Batch(c, P, "SELECT 1; DELETE FROM t", EffectEnum.Destructive);
            Batch(c, P, "SELECT 1; INSERT INTO t VALUES (1)", EffectEnum.Write);
            X(c, P, "SELECT 1; DROP TABLE t");
            X(c, M, "SELECT 1; SELECT 2");
            X(c, L, "SELECT 1; DROP TABLE t");
            Batch(c, S, "SELECT 1; UPDATE t SET a = 1", EffectEnum.Write);

            // Empty input is rejected; unparseable or unknown input fails closed as Destructive.
            X(c, P, "");
            X(c, P, "   ");
            X(c, P, "-- only a comment");
            X(c, P, ";;");
            D(c, P, "SELECT 'unterminated");
            D(c, P, "SELECT 1 /* unterminated");
            D(c, P, "SELECT $$ unterminated");
            D(c, S, "SELECT [unterminated");
            D(c, M, "SELECT `unterminated");
            D(c, P, "FROBNICATE the database");
            D(c, P, "42");
            D(c, P, "WITH x AS SELECT 1");
            D(c, P, "WITH x AS (SELECT 1)");
            D(c, P, "(((");
            D(c, M, "SELECT 1 /*! DROP TABLE t");
            D(c, P, "GOBBLEDYGOOK");
            D(c, S, "SELECT 1 /* unterminated");
            D(c, L, "SELECT \"unterminated");
            return c;
        }

        #endregion

        #region Private-Methods

        private static void R(List<ClassifierCase> list, SqlEngineEnum engine, string sql)
        {
            list.Add(new ClassifierCase(engine, sql, EffectEnum.Read, false, false));
        }

        private static void W(List<ClassifierCase> list, SqlEngineEnum engine, string sql)
        {
            list.Add(new ClassifierCase(engine, sql, EffectEnum.Write, false, false));
        }

        private static void D(List<ClassifierCase> list, SqlEngineEnum engine, string sql)
        {
            list.Add(new ClassifierCase(engine, sql, EffectEnum.Destructive, false, false));
        }

        private static void X(List<ClassifierCase> list, SqlEngineEnum engine, string sql)
        {
            list.Add(new ClassifierCase(engine, sql, EffectEnum.Destructive, true, false));
        }

        private static void Batch(List<ClassifierCase> list, SqlEngineEnum engine, string sql, EffectEnum expected)
        {
            list.Add(new ClassifierCase(engine, sql, expected, false, true));
        }

        #endregion
    }
}
