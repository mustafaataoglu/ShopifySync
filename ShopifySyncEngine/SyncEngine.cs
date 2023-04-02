using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ShopifySharp;
using ShopifySharp.Filters;

namespace ShopifySyncEngine
{
    public static class SyncEngine
    {
        public static async void Start()
        {
            const string _shopName = "https://authentic-1256.myshopify.com";
            const string _apiKey = "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";
            const string _password = "2270bb01343dc309d8674a2fedb2d85b";
            var products = new List<Product>();
            var service = new ProductService(_shopName, _apiKey);
            long? lastId = 0;
            // string connectionString = "Data Source=DESKTOP-1IHL5NT;Initial Catalog=SentezLive;Integrated Security=True;Timeout=180;";
            string connectionString = "Data Source=DESKTOP-1IHL5NT;Initial Catalog=SentezLive;User Id=syncuser;Password=!Sentez1234;Timeout=180;";

            //List<(string inventoryCode, string inventoryName, decimal actualStock, decimal price, string attachment)> sentezProducts = new List<(string, string, decimal, decimal, string)>();

            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), "ShopifyLogs");
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            log("Start");

            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();

                string sql = @"
                SELECT Erp_Inventory.InventoryCode as InventoryCode, Erp_Inventory.InventoryName as InventoryName, Erp_InventoryTotal.ActualStock as ActualStock, Erp_InventoryPriceList.Price as Price,Erp_InventoryAttachment.Attachment as Attachment,Erp_Mark.MarkName as MarkName
                FROM Erp_Inventory 
                JOIN Erp_InventoryTotal ON Erp_Inventory.RecId = Erp_InventoryTotal.InventoryID 
                JOIN Erp_InventoryPriceList ON Erp_Inventory.RecId = Erp_InventoryPriceList.InventoryID 
                JOIN Erp_InventoryAttachment ON Erp_Inventory.RecId = Erp_InventoryAttachment.InventoryID AND Erp_InventoryAttachment.Type = 1 
				Join Erp_Mark on Erp_Inventory.MarkId= Erp_Mark.RecId
                WHERE Erp_InventoryTotal.TotalDate IS NULL AND Erp_InventoryTotal.ActualStock >= 1 AND Erp_InventoryPriceList.PriceType = 2 AND Erp_InventoryTotal.WarehouseId = 12 ORDER BY Erp_Inventory.RecId;";

                SqlDataAdapter adapter = new SqlDataAdapter(sql, connection);
                DataTable dt = new DataTable();
                adapter.Fill(dt);
                log(dt.Rows.Count);
                log("Read Shopify");
                while (lastId >= 0)
                {
                    var filter = new ProductListFilter
                    {
                        SinceId = lastId
                    };
                    var productList = await service.ListAsync(filter);
                    if (productList != null && productList.Items.Any())
                    {
                        products.AddRange(productList.Items);
                        lastId = productList.Items.Last().Id;
                    }
                    else
                    {
                        break;
                    }


                }

                log($"Shopify Products {products.Count():n0}");
                await Task.Delay(5000);

                var variants = products.SelectMany(x => x.Variants).ToList();

                var sentezProducts = dt.Rows.Cast<DataRow>().ToList();
                for (int i = 0; i < sentezProducts.Count; i++)
                {
                    try
                    {
                        var sentezProduct = sentezProducts[i];
                        var shopifyProduct = variants.FirstOrDefault(p => p.SKU == sentezProduct.Field<string>("InventoryCode"));
                        if (shopifyProduct != null)
                        {
                            try
                            {
                                var product = products.Single(q => q.Id == shopifyProduct.ProductId);

                                product.Variants.First().Price = sentezProduct.Field<decimal?>("Price");
                                product.Variants.First().InventoryQuantity = (long)sentezProduct.Field<decimal?>("ActualStock");
                                product.Variants.First().Option1 = sentezProduct.Field<string>("MarkName");
                                await service.UpdateAsync(product.Id.Value, product);
                                log($"SKU : {shopifyProduct.SKU} güncellendi. {shopifyProduct.Price:c2} / {shopifyProduct.InventoryQuantity:n0}");

                                await Task.Delay(1000);
                            }
                            catch (Exception ex)
                            {
                                log("Update Err");
                                log(ex.Message);
                            }

                        }
                        else
                        {
                            try
                            {
                                //var imageSQL = $"select Attachment from Erp_InventoryAttachment where InventoryId = {sentezProduct.Field<long>("RecId")}";
                                //var imageSqlCommand = new SqlCommand(imageSQL, connection);

                                var product = new Product
                                {
                                    Title = sentezProduct.Field<string>("InventoryName"),
                                    Vendor = "Sentez",
                                    ProductType = "Sentez",
                                    Options = new List<ProductOption>
                                {
                                    new ProductOption
                                    {
                                        Name = "Marka"
                                    }
                                },

                                    Images = new List<ProductImage> {
                                                                    new ProductImage
                                                                    {
                                                                        Attachment = Convert.ToBase64String( sentezProduct.Field<byte[]>("Attachment")),
                                                                        Filename = sentezProduct.Field<string>("InventoryCode") + ".jpg",
                                                                        
                                                                        //Attachment = imageSqlCommand.ExecuteScalar().ToString()
                                                                       
                                                                    }
                                                                },

                                    Variants = new List<ProductVariant>
                                                                    {
                                                                        new ProductVariant
                                                                        {
                                                                            Title = sentezProduct.Field<string>("InventoryName"),
                                                                            FulfillmentService = "manual",
                                                                            InventoryManagement = "shopify",
                                                                            Option1 = sentezProduct.Field<string>("MarkName"),
                                                                            Price = sentezProduct.Field<decimal?>("Price"),
                                                                            SKU = sentezProduct.Field<string>("InventoryCode"),
                                                                            InventoryQuantity = (long)sentezProduct.Field<decimal?>("ActualStock")
                                                                        }
                                                                    }

                                };
                                await service.CreateAsync(product);
                                log($"SKU : {sentezProduct.Field<string>("InventoryName")} eklendi. {sentezProduct.Field<decimal?>("Price"):c2}");
                                await Task.Delay(1000);
                            }
                            catch (Exception ex)
                            {
                                log("Create Err");
                                log(ex.Message);
                            }

                        }
                    }
                    catch (Exception ex)
                    {
                        log(ex.Message);
                        throw;
                    }
                }

            }


            void log(object message)
            {
                var msg = $"[{DateTime.Now.ToLongTimeString()}] - {message}";

                Console.WriteLine(msg);
                File.AppendAllText(Path.Combine(folder, $"{DateTime.Now.ToString("yyyyMMdd")}.log.txt"), $"{msg}{Environment.NewLine}");
            }

        }
    }
}
