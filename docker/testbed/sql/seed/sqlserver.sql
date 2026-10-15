-- Hugo testbed "shop" schema (SQL Server). Generated from one dataset so every engine holds identical rows:
-- 8 customers, 10 products, 12 orders, 22 order items, one view, two secondary indexes, three foreign keys,
-- plus legacy_accounts (4 rows) and legacy_invoices (6 rows) joined by naming convention only.
-- Runs through sqlcmd from sqlserver-entrypoint.sh. Skips everything when the database already exists.

IF DB_ID(N'shop') IS NOT NULL
BEGIN
    PRINT 'shop already seeded';
    SET NOEXEC ON;
END
GO

CREATE DATABASE shop;
GO

USE shop;
GO

CREATE TABLE customers (
    customer_id INT NOT NULL PRIMARY KEY,
    email NVARCHAR(255) NOT NULL UNIQUE,
    full_name NVARCHAR(100) NOT NULL,
    country CHAR(2) NOT NULL,
    created_at DATETIME2 NOT NULL
);
GO

CREATE TABLE products (
    product_id INT NOT NULL PRIMARY KEY,
    sku NVARCHAR(32) NOT NULL UNIQUE,
    name NVARCHAR(100) NOT NULL,
    category NVARCHAR(50) NOT NULL,
    price DECIMAL(10,2) NOT NULL,
    stock INT NOT NULL DEFAULT 0
);
GO

CREATE TABLE orders (
    order_id INT NOT NULL PRIMARY KEY,
    customer_id INT NOT NULL,
    status NVARCHAR(20) NOT NULL,
    ordered_at DATETIME2 NOT NULL,
    total DECIMAL(10,2) NOT NULL,
    CONSTRAINT fk_orders_customer FOREIGN KEY (customer_id) REFERENCES customers (customer_id)
);
GO

CREATE TABLE order_items (
    order_item_id INT NOT NULL PRIMARY KEY,
    order_id INT NOT NULL,
    product_id INT NOT NULL,
    quantity INT NOT NULL,
    unit_price DECIMAL(10,2) NOT NULL,
    CONSTRAINT fk_order_items_order FOREIGN KEY (order_id) REFERENCES orders (order_id),
    CONSTRAINT fk_order_items_product FOREIGN KEY (product_id) REFERENCES products (product_id)
);
GO

CREATE INDEX idx_orders_customer_id ON orders (customer_id);
GO

CREATE INDEX idx_order_items_order_id ON order_items (order_id);
GO

CREATE VIEW customer_order_totals AS
SELECT c.customer_id, c.full_name, COUNT(o.order_id) AS order_count, COALESCE(SUM(o.total), 0) AS lifetime_total
FROM customers c
LEFT JOIN orders o ON o.customer_id = c.customer_id
GROUP BY c.customer_id, c.full_name;
GO

INSERT INTO customers (customer_id, email, full_name, country, created_at) VALUES
    (1, 'ada.lovelace@example.com', 'Ada Lovelace', 'GB', '2025-01-05 09:12:00'),
    (2, 'grace.hopper@example.com', 'Grace Hopper', 'US', '2025-01-18 14:30:00'),
    (3, 'alan.turing@example.com', 'Alan Turing', 'GB', '2025-02-02 11:00:00'),
    (4, 'katherine.johnson@example.com', 'Katherine Johnson', 'US', '2025-02-20 16:45:00'),
    (5, 'linus.torvalds@example.com', 'Linus Torvalds', 'FI', '2025-03-11 08:05:00'),
    (6, 'margaret.hamilton@example.com', 'Margaret Hamilton', 'US', '2025-03-29 19:20:00'),
    (7, 'tim.berners-lee@example.com', 'Tim Berners-Lee', 'GB', '2025-04-14 10:10:00'),
    (8, 'barbara.liskov@example.com', 'Barbara Liskov', 'US', '2025-05-02 13:55:00');
GO

INSERT INTO products (product_id, sku, name, category, price, stock) VALUES
    (1, 'KB-001', 'Mechanical Keyboard', 'Peripherals', 89.00, 120),
    (2, 'MS-002', 'Wireless Mouse', 'Peripherals', 29.50, 300),
    (3, 'MN-003', '27in 4K Monitor', 'Displays', 349.99, 45),
    (4, 'HD-004', 'USB-C Hub', 'Accessories', 39.95, 210),
    (5, 'LT-005', 'Laptop Stand', 'Accessories', 24.00, 160),
    (6, 'HS-006', 'Noise Cancelling Headset', 'Audio', 159.00, 75),
    (7, 'WC-007', '1080p Webcam', 'Video', 64.99, 90),
    (8, 'SS-008', '1TB Portable SSD', 'Storage', 109.00, 130),
    (9, 'CB-009', 'Cable Organizer Kit', 'Accessories', 12.75, 400),
    (10, 'DL-010', 'LED Desk Lamp', 'Lighting', 45.00, 0);
GO

INSERT INTO orders (order_id, customer_id, status, ordered_at, total) VALUES
    (1, 1, 'delivered', '2025-06-01 10:00:00', 118.50),
    (2, 2, 'delivered', '2025-06-03 15:20:00', 699.98),
    (3, 1, 'shipped', '2025-06-10 09:45:00', 147.25),
    (4, 3, 'delivered', '2025-06-12 18:30:00', 198.95),
    (5, 4, 'cancelled', '2025-06-15 12:00:00', 48.00),
    (6, 5, 'delivered', '2025-06-20 07:15:00', 166.74),
    (7, 6, 'processing', '2025-07-01 11:11:00', 429.89),
    (8, 2, 'shipped', '2025-07-04 20:00:00', 83.00),
    (9, 7, 'delivered', '2025-07-09 08:40:00', 159.00),
    (10, 3, 'processing', '2025-07-15 14:05:00', 243.50),
    (11, 8, 'pending', '2025-07-20 16:16:00', 104.94),
    (12, 1, 'pending', '2025-07-22 09:30:00', 74.50);
GO

INSERT INTO order_items (order_item_id, order_id, product_id, quantity, unit_price) VALUES
    (1, 1, 1, 1, 89.00),
    (2, 1, 2, 1, 29.50),
    (3, 2, 3, 2, 349.99),
    (4, 3, 8, 1, 109.00),
    (5, 3, 9, 3, 12.75),
    (6, 4, 6, 1, 159.00),
    (7, 4, 4, 1, 39.95),
    (8, 5, 5, 2, 24.00),
    (9, 6, 1, 1, 89.00),
    (10, 6, 7, 1, 64.99),
    (11, 6, 9, 1, 12.75),
    (12, 7, 3, 1, 349.99),
    (13, 7, 4, 2, 39.95),
    (14, 8, 2, 2, 29.50),
    (15, 8, 5, 1, 24.00),
    (16, 9, 6, 1, 159.00),
    (17, 10, 8, 2, 109.00),
    (18, 10, 9, 2, 12.75),
    (19, 11, 7, 1, 64.99),
    (20, 11, 4, 1, 39.95),
    (21, 12, 10, 1, 45.00),
    (22, 12, 2, 1, 29.50);
GO

-- Legacy pair with no declared foreign keys: legacy_invoices.account_id joins legacy_accounts.account_id by naming
-- convention only, so relationship inference has something to find. Its status, activity, owner, amount, and
-- timestamp columns also make terms such as status, active, owner, and latest ambiguous across the schema.
CREATE TABLE legacy_accounts (
    account_id INT NOT NULL PRIMARY KEY,
    account_name NVARCHAR(100) NOT NULL,
    status NVARCHAR(20) NOT NULL,
    is_active BIT NOT NULL,
    owner_name NVARCHAR(100) NOT NULL,
    opened_at DATETIME2 NOT NULL,
    deleted_at DATETIME2 NULL
);
GO

CREATE TABLE legacy_invoices (
    invoice_id INT NOT NULL PRIMARY KEY,
    account_id INT NOT NULL,
    status NVARCHAR(20) NOT NULL,
    amount DECIMAL(10,2) NOT NULL,
    issued_at DATETIME2 NOT NULL,
    created_by NVARCHAR(100) NOT NULL
);
GO

CREATE INDEX idx_legacy_invoices_account_id ON legacy_invoices (account_id);
GO

INSERT INTO legacy_accounts (account_id, account_name, status, is_active, owner_name, opened_at, deleted_at) VALUES
    (1, 'Northwind Traders', 'open', 1, 'Ada Lovelace', '2024-01-10 09:00:00', NULL),
    (2, 'Contoso Ltd', 'open', 1, 'Grace Hopper', '2024-02-14 10:30:00', NULL),
    (3, 'Fabrikam Inc', 'closed', 0, 'Alan Turing', '2024-03-01 08:15:00', '2025-01-15 17:00:00'),
    (4, 'Tailspin Toys', 'suspended', 1, 'Grace Hopper', '2024-05-20 14:45:00', NULL);
GO

INSERT INTO legacy_invoices (invoice_id, account_id, status, amount, issued_at, created_by) VALUES
    (1, 1, 'paid', 1200.00, '2025-01-05 12:00:00', 'billing'),
    (2, 1, 'paid', 800.00, '2025-02-05 12:00:00', 'billing'),
    (3, 2, 'open', 450.50, '2025-02-10 12:00:00', 'billing'),
    (4, 3, 'void', 300.00, '2025-01-20 12:00:00', 'ops'),
    (5, 4, 'paid', 99.99, '2025-03-01 12:00:00', 'billing'),
    (6, 2, 'paid', 150.00, '2025-03-03 12:00:00', 'ops');
GO

UPDATE STATISTICS customers;
UPDATE STATISTICS orders;
UPDATE STATISTICS legacy_accounts;
UPDATE STATISTICS legacy_invoices;
GO

SET NOEXEC OFF;
GO
