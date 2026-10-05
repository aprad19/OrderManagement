CREATE TABLE Products (
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    Name nvarchar(200) NOT NULL,
    Price decimal(18,2) NOT NULL,
    StockQuantity int NOT NULL,
    RowVersion rowversion NOT NULL,
    CONSTRAINT CK_Products_Stock_NonNegative CHECK (StockQuantity >= 0)
);

CREATE TABLE Orders (
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    CustomerId uniqueidentifier NOT NULL,
    ShippingAddress nvarchar(500) NOT NULL,
    Status int NOT NULL,
    CreatedAt datetime2 NOT NULL,
    UpdatedAt datetime2 NOT NULL,
    RowVersion rowversion NOT NULL
);

CREATE INDEX IX_Orders_CustomerId ON Orders(CustomerId);
CREATE INDEX IX_Orders_Status ON Orders(Status);
CREATE INDEX IX_Orders_CreatedAt ON Orders(CreatedAt);
CREATE INDEX IX_Orders_CustomerId_CreatedAt ON Orders(CustomerId, CreatedAt);

CREATE TABLE OrderItems (
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    OrderId uniqueidentifier NOT NULL,
    ProductId uniqueidentifier NOT NULL,
    Quantity int NOT NULL,
    UnitPrice decimal(18,2) NOT NULL,
    CONSTRAINT FK_OrderItems_Orders FOREIGN KEY (OrderId) REFERENCES Orders(Id),
    CONSTRAINT FK_OrderItems_Products FOREIGN KEY (ProductId) REFERENCES Products(Id)
);
CREATE UNIQUE INDEX IX_OrderItems_OrderId_ProductId ON OrderItems(OrderId, ProductId);

CREATE TABLE IdempotencyKeys (
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    [Key] nvarchar(200) NOT NULL,
    RequestHash nvarchar(64) NOT NULL,
    OrderId uniqueidentifier NULL,
    CreatedAt datetime2 NOT NULL,
    CONSTRAINT FK_IdempotencyKeys_Orders FOREIGN KEY (OrderId) REFERENCES Orders(Id)
);
CREATE UNIQUE INDEX IX_IdempotencyKeys_Key ON IdempotencyKeys([Key]);
CREATE INDEX IX_IdempotencyKeys_CreatedAt ON IdempotencyKeys(CreatedAt);
