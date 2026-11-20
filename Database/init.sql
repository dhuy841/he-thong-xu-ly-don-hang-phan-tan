CREATE TABLE IF NOT EXISTS orders (
    id uuid PRIMARY KEY,
    customer text NOT NULL,
    items jsonb NOT NULL,
    status text NOT NULL CHECK (status IN ('Pending', 'Processing', 'Completed', 'Failed')),
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_orders_created_at ON orders (created_at DESC);
