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
using System.Runtime.Remoting.Contexts;

namespace ShopifySyncEngine
{
    public static class SyncEngine
    {
        public static async void Start()
        {
            const string _shopName = "https://authentic-1612.myshopify.com/";
            const string _apiKey = "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";
            const string _password = "957fb6957ff9c2aaa6f350c36b493847";
            var products = new List<Product>();
            var service = new ProductService(_shopName, _apiKey);
            var vservice = new InventoryLevelService(_shopName, _apiKey);
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
                SELECT Erp_Inventory.InventoryCode as InventoryCode, Erp_Inventory.InventoryName as InventoryName, Erp_InventoryTotal.ActualStock as ActualStock, Erp_InventoryPriceList.Price as Price,Erp_InventoryAttachment.Attachment as Attachment,Erp_Mark.MarkName as MarkName,Erp_InventoryGroup.GroupName as GroupName,Erp_Category.CategoryName2 as CategoryName2
                FROM Erp_Inventory 
                JOIN Erp_InventoryTotal ON Erp_Inventory.RecId = Erp_InventoryTotal.InventoryID 
				Join Erp_Category On Erp_Inventory.CategoryId=Erp_Category.RecId
                JOIN Erp_InventoryPriceList ON Erp_Inventory.RecId = Erp_InventoryPriceList.InventoryID 
                JOIN Erp_InventoryAttachment ON Erp_Inventory.RecId = Erp_InventoryAttachment.InventoryID AND Erp_InventoryAttachment.Type = 1 
				Join Erp_Mark on Erp_Inventory.MarkId= Erp_Mark.RecId
				join Erp_InventoryGroup on Erp_Inventory.GroupId=Erp_InventoryGroup.RecId
                WHERE Erp_InventoryTotal.TotalDate IS NULL AND Erp_InventoryTotal.ActualStock >= 0 AND Erp_InventoryPriceList.PriceType = 2 AND Erp_InventoryTotal.WarehouseId = 12 ORDER BY Erp_Inventory.RecId;";

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
                                product.Title = sentezProduct.Field<string>("MarkName") + " " + sentezProduct.Field<string>("InventoryName") + " " + sentezProduct.Field<string>("CategoryName2");
                                product.Variants.First().Price = sentezProduct.Field<decimal?>("Price") * 0.75m;
                                product.Variants.First().InventoryQuantity = Convert.ToInt64(sentezProduct.Field<decimal?>("ActualStock"));
                                product.Options.First().Name = "Brand";
                                product.Variants.First().Option1 = sentezProduct.Field<string>("MarkName");
                                product.Tags = sentezProduct.Field<string>("GroupName") + "," + sentezProduct.Field<string>("InventoryName") + "," + sentezProduct.Field<string>("CategoryName2");
                                await service.UpdateAsync(product.Id.Value, product);
                                var inventoryLevel = new InventoryLevel
                                {
                                    LocationId = 81005871445,
                                    InventoryItemId = product.Variants.First().InventoryItemId,
                                    Available = Convert.ToInt64(sentezProduct.Field<decimal?>("ActualStock"))
                                };
                                await vservice.SetAsync(inventoryLevel);


                                log($"SKU : {shopifyProduct.SKU} güncellendi. {shopifyProduct.Price:c2} / {shopifyProduct.InventoryQuantity:n0}");

                                await Task.Delay(1000);
                            }
                            catch (Exception ex)
                            {
                                log("Update Err");
                                log(ex.Message);
                            }

                        }
                        else if ((long)sentezProduct.Field<decimal?>("ActualStock") >= 1)
                        {
                            try
                            {


                                var product = new Product
                                {
                                    Title = sentezProduct.Field<string>("MarkName") + " " + sentezProduct.Field<string>("InventoryName") + " " + sentezProduct.Field<string>("CategoryName2"),
                                    Vendor = "Sentez",
                                    ProductType = "Sentez",
                                    Tags = sentezProduct.Field<string>("GroupName") + "," + sentezProduct.Field<string>("InventoryName") + "," + sentezProduct.Field<string>("CategoryName2"),
                                    
                                Options = new List<ProductOption>
                                {
                                    new ProductOption
                                    {
                                        Name = "Brand"

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
                                                                            
                                                                            FulfillmentService = "manual",
                                                                            InventoryManagement = "shopify",
                                                                            Option1 = sentezProduct.Field<string>("MarkName"),
                                                                            Price = sentezProduct.Field<decimal?>("Price") * 0.75m,
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
