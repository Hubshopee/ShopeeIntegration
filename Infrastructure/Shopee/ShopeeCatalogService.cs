using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Infrastructure.Shopee;

public class ShopeeCatalogService
{
    private readonly HttpClient _http;
    private readonly ShopeeConfig _config;

    public ShopeeCatalogService(HttpClient http, Microsoft.Extensions.Options.IOptions<ShopeeConfig> config)
    {
        _http = http;
        _config = config.Value;
    }

    public async Task<IReadOnlyList<string>> UploadImages(
        string accessToken,
        int partnerId,
        string partnerKey,
        int shopId,
        IReadOnlyList<string> imageUrls,
        CancellationToken cancellationToken)
    {
        var imageIds = new List<string>();

        foreach (var imageUrl in imageUrls)
        {
            imageIds.Add(await UploadImage(accessToken, partnerId, partnerKey, shopId, imageUrl, cancellationToken));
        }

        return imageIds;
    }

    public async Task<long> CreateItem(
        string accessToken,
        int partnerId,
        string partnerKey,
        int shopId,
        ShopeeCreateItemRequest item,
        CancellationToken cancellationToken)
    {
        var path = "/api/v2/product/add_item";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sign = ShopeeSigner.Generate(partnerKey, $"{partnerId}{path}{timestamp}{accessToken}{shopId}");
        var url = BuildSignedUrl(path, partnerId, timestamp, sign, accessToken, shopId);
        var payload = JsonSerializer.Serialize(item, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            Version = new Version(1, 1),
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };

        request.Headers.ExpectContinue = false;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        request.Headers.TryAddWithoutValidation("User-Agent", "curl/8.0.0");

        var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if ((int)response.StatusCode == 405)
            return await CreateItemWithCurl(url, payload, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Shopee add item falhou. Status: {(int)response.StatusCode}. Payload: {payload}. Resposta: {body}");

        try
        {
            return ExtractItemId(body);
        }
        catch (Exception ex)
        {
            throw new Exception($"Shopee add item rejeitou o payload. Payload: {payload}. Body: {body}", ex);
        }
    }

    public async Task UpdateStock(
        string accessToken,
        int partnerId,
        string partnerKey,
        int shopId,
        ShopeeUpdateStockRequest item,
        CancellationToken cancellationToken)
    {
        var payloads = new object[]
        {
            item,
            new
            {
                item_id = item.ItemId,
                stock_list = new[]
                {
                    new
                    {
                        seller_stock = item.SellerStock.Select(x => new
                        {
                            location_id = x.LocationId,
                            stock = x.Stock
                        }).ToArray()
                    }
                }
            },
            new
            {
                item_id = item.ItemId,
                stock_list = new[]
                {
                    new
                    {
                        normal_stock = item.Stock
                    }
                }
            },
            new
            {
                item_id = item.ItemId,
                stock_list = new[]
                {
                    new
                    {
                        model_id = 0,
                        seller_stock = item.SellerStock.Select(x => new
                        {
                            location_id = x.LocationId,
                            stock = x.Stock
                        }).ToArray()
                    }
                }
            },
            new
            {
                item_id = item.ItemId,
                stock_list = new[]
                {
                    new
                    {
                        model_id = 0,
                        normal_stock = item.Stock
                    }
                }
            }
        };

        Exception? lastError = null;

        foreach (var payload in payloads)
        {
            try
            {
                await SendJsonRequest(
                    "/api/v2/product/update_stock",
                    accessToken,
                    partnerId,
                    partnerKey,
                    shopId,
                    payload,
                    "update stock",
                    cancellationToken
                );
                return;
            }
            catch (Exception ex) when (
                ex.Message.Contains("update stock", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("failure_list", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("success_list", StringComparison.OrdinalIgnoreCase))
            {
                lastError = ex;
            }
        }

        if (lastError is not null)
            throw lastError;

        throw new Exception("Shopee update stock falhou sem retorno detalhado.");
    }

    public async Task UpdatePrice(
        string accessToken,
        int partnerId,
        string partnerKey,
        int shopId,
        ShopeeUpdatePriceRequest item,
        CancellationToken cancellationToken)
    {
        var payloads = new object[]
        {
            item,
            new
            {
                item_id = item.ItemId,
                price_list = new[]
                {
                    new
                    {
                        original_price = item.OriginalPrice
                    }
                }
            },
            new
            {
                item_id = item.ItemId,
                price_list = new[]
                {
                    new
                    {
                        model_id = 0,
                        original_price = item.OriginalPrice
                    }
                }
            },
            new
            {
                item_id = item.ItemId,
                price_info = new[]
                {
                    new
                    {
                        original_price = item.OriginalPrice
                    }
                }
            },
            new
            {
                item_id = item.ItemId,
                price_info = new[]
                {
                    new
                    {
                        model_id = 0,
                        original_price = item.OriginalPrice
                    }
                }
            }
        };

        Exception? lastError = null;

        foreach (var payload in payloads)
        {
            try
            {
                await SendJsonRequest(
                    "/api/v2/product/update_price",
                    accessToken,
                    partnerId,
                    partnerKey,
                    shopId,
                    payload,
                    "update price",
                    cancellationToken
                );
                return;
            }
            catch (Exception ex) when (ex.Message.Contains("product.error_update_price_fail", StringComparison.OrdinalIgnoreCase))
            {
                lastError = ex;
            }
        }

        if (lastError is not null)
            throw lastError;

        throw new Exception("Shopee update price falhou sem retorno detalhado.");
    }

    public async Task UpdateItem(
        string accessToken,
        int partnerId,
        string partnerKey,
        int shopId,
        ShopeeUpdateItemRequest item,
        CancellationToken cancellationToken)
    {
        await SendJsonRequest(
            "/api/v2/product/update_item",
            accessToken,
            partnerId,
            partnerKey,
            shopId,
            item,
            "update item",
            cancellationToken
        );
    }

    public async Task DeleteItem(
        string accessToken,
        int partnerId,
        string partnerKey,
        int shopId,
        ShopeeDeleteItemRequest item,
        CancellationToken cancellationToken)
    {
        var payloads = new object[]
        {
            item,
            new
            {
                item_id = item.ItemId
            },
            new
            {
                item_id_list = new[] { item.ItemId }
            }
        };

        Exception? lastError = null;

        foreach (var payload in payloads)
        {
            try
            {
                await SendJsonRequest(
                    "/api/v2/product/delete_item",
                    accessToken,
                    partnerId,
                    partnerKey,
                    shopId,
                    payload,
                    "delete item",
                    cancellationToken
                );
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        if (lastError is not null)
            throw lastError;

        throw new Exception("Shopee delete item falhou sem retorno detalhado.");
    }

    public async Task<IReadOnlyList<ShopeeOrderSummary>> GetOrdersUpdatedSince(
        string accessToken,
        int partnerId,
        string partnerKey,
        int shopId,
        DateTime? dataInicial,
        DateTime? dataFinal,
        CancellationToken cancellationToken)
    {
        var inicio = (dataInicial ?? DateTime.Now.AddDays(-15)).ToUniversalTime();
        var fim = (dataFinal ?? DateTime.Now).ToUniversalTime();
        var agoraUtc = DateTime.UtcNow;

        if (fim > agoraUtc)
            fim = agoraUtc;

        if (inicio >= fim)
            inicio = fim.AddMinutes(-5);

        var janelaMaxima = TimeSpan.FromDays(15) - TimeSpan.FromMinutes(1);

        if (fim - inicio > janelaMaxima)
            inicio = fim - janelaMaxima;

        if (inicio >= fim)
            inicio = fim.AddMinutes(-1);

        var pedidos = new Dictionary<string, ShopeeOrderSummary>(StringComparer.OrdinalIgnoreCase);
        var cursor = string.Empty;
        var continuar = true;

        while (continuar)
        {
            var body = await SendGetRequest(
                "/api/v2/order/get_order_list",
                accessToken,
                partnerId,
                partnerKey,
                shopId,
                "get_order_list",
                new Dictionary<string, string?>
                {
                    ["time_range_field"] = "update_time",
                    ["time_from"] = ((DateTimeOffset)inicio).ToUnixTimeSeconds().ToString(),
                    ["time_to"] = ((DateTimeOffset)fim).ToUnixTimeSeconds().ToString(),
                    ["page_size"] = "100",
                    ["cursor"] = string.IsNullOrWhiteSpace(cursor) ? null : cursor,
                    ["response_optional_fields"] = "order_status"
                },
                cancellationToken
            );

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            ValidarRespostaApi(root, body, "get_order_list");

            if (!root.TryGetProperty("response", out var response))
                break;

            if (response.TryGetProperty("order_list", out var orderList))
            {
                foreach (var order in orderList.EnumerateArray())
                {
                    var orderSn = GetString(order, "order_sn");

                    if (string.IsNullOrWhiteSpace(orderSn))
                        continue;

                    pedidos[orderSn] = new ShopeeOrderSummary
                    {
                        OrderSn = orderSn,
                        CreateTime = GetDateTime(order, "create_time"),
                        UpdateTime = GetDateTime(order, "update_time"),
                        OrderStatus = GetString(order, "order_status")
                    };
                }
            }

            continuar = response.TryGetProperty("more", out var moreElement) && moreElement.ValueKind == JsonValueKind.True;
            cursor = response.TryGetProperty("next_cursor", out var cursorElement)
                ? cursorElement.GetString() ?? string.Empty
                : string.Empty;

            if (string.IsNullOrWhiteSpace(cursor))
                continuar = false;
        }

        return pedidos.Values
            .OrderBy(x => x.UpdateTime ?? x.CreateTime)
            .ToList();
    }

    public async Task<IReadOnlyList<ShopeeOrderDetail>> GetOrderDetails(
        string accessToken,
        int partnerId,
        string partnerKey,
        int shopId,
        IReadOnlyCollection<string> orderSns,
        CancellationToken cancellationToken)
    {
        var detalhes = new List<ShopeeOrderDetail>();
        var lotes = orderSns
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Chunk(50);

        foreach (var lote in lotes)
        {
            var body = await SendGetRequest(
                "/api/v2/order/get_order_detail",
                accessToken,
                partnerId,
                partnerKey,
                shopId,
                "get_order_detail",
                new Dictionary<string, string?>
                {
                    ["order_sn_list"] = string.Join(",", lote),
                    ["response_optional_fields"] = "buyer_user_id,recipient_address,item_list,total_amount,estimated_shipping_fee,actual_shipping_fee,order_status,package_list,create_time,update_time"
                },
                cancellationToken
            );

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            ValidarRespostaApi(root, body, "get_order_detail");

            if (!root.TryGetProperty("response", out var response)
                || !response.TryGetProperty("order_list", out var orderList))
            {
                continue;
            }

            foreach (var order in orderList.EnumerateArray())
            {
                detalhes.Add(ParseOrderDetail(order));
            }
        }

        return detalhes;
    }

    public async Task<long> ResolveLogisticsChannelId(
        string accessToken,
        int partnerId,
        string partnerKey,
        int shopId,
        long? preferredChannelId,
        CancellationToken cancellationToken)
    {
        if (preferredChannelId.HasValue && preferredChannelId.Value > 0)
            return preferredChannelId.Value;

        var channels = await GetLogisticsChannels(accessToken, partnerId, partnerKey, shopId, cancellationToken);
        var eligibleChannels = channels
            .Where(x => x.Enabled && x.LogisticsCapability?.SellerLogistics == true)
            .ToList();

        var selected = eligibleChannels.FirstOrDefault();

        if (selected is not null)
            return selected.LogisticsChannelId;

        var availableChannels = channels
            .Where(x => x.Enabled)
            .Select(x => $"{x.LogisticsChannelId}:{x.LogisticsChannelName}")
            .ToList();

        var channelsDescription = availableChannels.Count == 0
            ? "nenhum canal habilitado"
            : string.Join(", ", availableChannels);

        throw new Exception(
            $"Nenhum canal logistico seller_logistics habilitado para a loja no sandbox. Canais habilitados encontrados: {channelsDescription}."
        );
    }

    private async Task<string> UploadImage(
        string accessToken,
        int partnerId,
        string partnerKey,
        int shopId,
        string imageUrl,
        CancellationToken cancellationToken)
    {
        var path = "/api/v2/media_space/upload_image";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sign = ShopeeSigner.Generate(partnerKey, $"{partnerId}{path}{timestamp}{accessToken}{shopId}");
        var url = BuildSignedUrl(path, partnerId, timestamp, sign, accessToken, shopId);

        var imageBytes = await DownloadImageBytes(imageUrl, cancellationToken);
        var fileName = Path.GetFileName(new Uri(imageUrl).LocalPath);

        using var form = new MultipartFormDataContent();
        using var imageContent = new ByteArrayContent(imageBytes);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(imageContent, "image", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = form,
            Version = new Version(1, 1),
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };

        request.Headers.ExpectContinue = false;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        request.Headers.TryAddWithoutValidation("User-Agent", "curl/8.0.0");

        try
        {
            var response = await _http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if ((int)response.StatusCode == 405)
                return await UploadImageWithCurl(url, imageBytes, fileName, imageUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Shopee upload image falhou. UrlImagem: {imageUrl}. Status: {(int)response.StatusCode}. Resposta: {body}");

            return ExtractImageId(body);
        }
        catch (HttpRequestException)
        {
            return await UploadImageWithCurl(url, imageBytes, fileName, imageUrl, cancellationToken);
        }
    }

    private static string ExtractImageId(string body)
    {
        var root = JsonNode.Parse(body) ?? throw new Exception("Resposta vazia ao enviar imagem.");
        var imageId =
            TryGetString(root["response"]?["image_info"]?["image_id"])
            ?? TryGetString(root["response"]?["image_id"])
            ?? TryGetString(root["image_id"])
            ?? TryGetString(root["response"]?["image_info_list"]?[0]?["image_info"]?["image_id"]);

        if (string.IsNullOrWhiteSpace(imageId))
            throw new Exception($"Shopee nao retornou image_id no upload. Body: {body}");

        return imageId;
    }

    private async Task SendJsonRequest(
        string path,
        string accessToken,
        int partnerId,
        string partnerKey,
        int shopId,
        object payloadObject,
        string operation,
        CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sign = ShopeeSigner.Generate(partnerKey, $"{partnerId}{path}{timestamp}{accessToken}{shopId}");
        var url = BuildSignedUrl(path, partnerId, timestamp, sign, accessToken, shopId);
        var payload = JsonSerializer.Serialize(payloadObject, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            Version = new Version(1, 1),
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };

        request.Headers.ExpectContinue = false;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        request.Headers.TryAddWithoutValidation("User-Agent", "curl/8.0.0");

        var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if ((int)response.StatusCode == 405)
        {
            body = await SendPostWithCurl(url, payload, operation, cancellationToken);
        }
        else if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Shopee {operation} falhou. Payload: {payload}. Resposta: {body}");
        }

        ValidarRespostaGenerica(body, operation, payload);
    }

    private async Task<IReadOnlyList<ShopeeLogisticsChannel>> GetLogisticsChannels(
        string accessToken,
        int partnerId,
        string partnerKey,
        int shopId,
        CancellationToken cancellationToken)
    {
        var path = "/api/v2/logistics/get_channel_list";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sign = ShopeeSigner.Generate(partnerKey, $"{partnerId}{path}{timestamp}{accessToken}{shopId}");
        var url = BuildSignedUrl(path, partnerId, timestamp, sign, accessToken, shopId);

        using var request = new HttpRequestMessage(HttpMethod.Get, url)
        {
            Version = new Version(1, 1),
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };

        request.Headers.ExpectContinue = false;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        request.Headers.TryAddWithoutValidation("User-Agent", "curl/8.0.0");

        var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if ((int)response.StatusCode == 405)
            body = await SendGetWithCurl(url, "consultar canais logísticos", cancellationToken);
        else if (!response.IsSuccessStatusCode)
            throw new Exception($"Shopee get channel list falhou. Status: {(int)response.StatusCode}. Resposta: {body}");

        var root = JsonNode.Parse(body) ?? throw new Exception("Resposta vazia ao consultar canais logísticos.");
        var listNode = root["response"]?["logistics_channel_list"];
        var channels = listNode?.Deserialize<List<ShopeeLogisticsChannel>>(JsonOptions);

        return channels ?? [];
    }

    private static async Task<byte[]> DownloadImageBytes(string imageUrl, CancellationToken cancellationToken)
    {
        using var downloadClient = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl)
        {
            Version = new Version(1, 1),
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };

        request.Headers.ExpectContinue = false;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
        request.Headers.Referrer = new Uri("http://loja.umec.com.br/");

        try
        {
            using var response = await downloadClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );

            if (response.IsSuccessStatusCode)
                return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch
        {
        }

        return await DownloadImageBytesWithCurl(imageUrl, cancellationToken);
    }

    private static async Task<byte[]> DownloadImageBytesWithCurl(string imageUrl, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "curl.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-sS");
        startInfo.ArgumentList.Add("-L");
        startInfo.ArgumentList.Add("-A");
        startInfo.ArgumentList.Add("Mozilla/5.0");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add("http://loja.umec.com.br/");
        startInfo.ArgumentList.Add(imageUrl);

        using var process = Process.Start(startInfo)
            ?? throw new Exception($"Nao foi possivel iniciar curl.exe para baixar imagem: {imageUrl}");

        await using var output = new MemoryStream();
        var copyTask = process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync();

        await Task.WhenAll(copyTask, process.WaitForExitAsync(cancellationToken));
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
            throw new Exception(
                $"Falha ao baixar imagem via curl. UrlImagem: {imageUrl}. ExitCode: {process.ExitCode}. Erro: {stderr}"
            );

        var imageBytes = output.ToArray();

        if (imageBytes.Length == 0)
            throw new Exception($"Curl retornou imagem vazia. UrlImagem: {imageUrl}");

        return imageBytes;
    }

    private static string? TryGetString(JsonNode? node)
    {
        if (node is null)
            return null;

        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var stringValue) && !string.IsNullOrWhiteSpace(stringValue))
                return stringValue;

            if (value.TryGetValue<long>(out var longValue))
                return longValue.ToString();
        }

        return null;
    }

    private static long ExtractItemId(string body)
    {
        if (body.Contains("logistics.no.valid.channel", StringComparison.OrdinalIgnoreCase))
            throw new Exception($"Shopee sem canal logístico habilitado para o item. Body: {body}");

        var root = JsonNode.Parse(body) ?? throw new Exception("Resposta vazia ao cadastrar item.");
        var itemId = TryGetLong(root["response"]?["item_id"])
            ?? TryGetLong(root["item_id"]);

        if (!itemId.HasValue)
            throw new Exception($"Shopee nao retornou item_id no cadastro. Body: {body}");

        return itemId.Value;
    }

    private static void ValidarRespostaGenerica(string body, string operation, string payload)
    {
        var root = JsonNode.Parse(body) ?? throw new Exception($"Resposta vazia ao executar {operation}.");
        var error = TryGetString(root["error"]);
        var successList = root["response"]?["success_list"]?.AsArray();
        var failureList = root["response"]?["failure_list"]?.AsArray();

        if (!string.IsNullOrWhiteSpace(error))
            throw new Exception($"Shopee {operation} rejeitou o payload. Payload: {payload}. Body: {body}");

        if (failureList is not null && failureList.Count > 0)
            throw new Exception($"Shopee {operation} retornou failure_list. Payload: {payload}. Body: {body}");

        if (successList is not null && failureList is not null && successList.Count == 0)
            throw new Exception($"Shopee {operation} nao confirmou sucesso no success_list. Payload: {payload}. Body: {body}");
    }

    private static long? TryGetLong(JsonNode? node)
    {
        if (node is null)
            return null;

        if (node is JsonValue value)
        {
            if (value.TryGetValue<long>(out var longValue))
                return longValue;

            if (value.TryGetValue<string>(out var stringValue) && long.TryParse(stringValue, out var parsed))
                return parsed;
        }

        return null;
    }

    private static async Task<string> UploadImageWithCurl(
        string url,
        byte[] imageBytes,
        string fileName,
        string imageUrl,
        CancellationToken cancellationToken)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}_{fileName}");

        try
        {
            await File.WriteAllBytesAsync(tempFile, imageBytes, cancellationToken);

            var startInfo = new ProcessStartInfo
            {
                FileName = "curl.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-sS");
            startInfo.ArgumentList.Add("-k");
            startInfo.ArgumentList.Add("-X");
            startInfo.ArgumentList.Add("POST");
            startInfo.ArgumentList.Add(url);
            startInfo.ArgumentList.Add("-H");
            startInfo.ArgumentList.Add("Accept: */*");
            startInfo.ArgumentList.Add("-A");
            startInfo.ArgumentList.Add("curl/8.0.0");
            startInfo.ArgumentList.Add("-F");
            startInfo.ArgumentList.Add($"image=@{tempFile};type=image/jpeg;filename={fileName}");

            using var process = Process.Start(startInfo)
                ?? throw new Exception($"Nao foi possivel iniciar curl.exe para enviar imagem: {imageUrl}");

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
                throw new Exception(
                    $"Shopee upload image falhou via curl. UrlImagem: {imageUrl}. ExitCode: {process.ExitCode}. Erro: {stderr}"
                );

            return ExtractImageId(stdout);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    private static async Task<long> CreateItemWithCurl(
        string url,
        string payload,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "curl.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-sS");
        startInfo.ArgumentList.Add("-k");
        startInfo.ArgumentList.Add("-X");
        startInfo.ArgumentList.Add("POST");
        startInfo.ArgumentList.Add(url);
        startInfo.ArgumentList.Add("-H");
        startInfo.ArgumentList.Add("Accept: */*");
        startInfo.ArgumentList.Add("-H");
        startInfo.ArgumentList.Add("Content-Type: application/json");
        startInfo.ArgumentList.Add("-A");
        startInfo.ArgumentList.Add("curl/8.0.0");
        startInfo.ArgumentList.Add("-d");
        startInfo.ArgumentList.Add(payload);

        using var process = Process.Start(startInfo)
            ?? throw new Exception("Nao foi possivel iniciar curl.exe para cadastrar item na Shopee.");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
            throw new Exception(
                $"Shopee add item falhou via curl. Payload: {payload}. ExitCode: {process.ExitCode}. Erro: {stderr}"
            );

        try
        {
            return ExtractItemId(stdout);
        }
        catch (Exception ex)
        {
            throw new Exception($"Shopee add item rejeitou o payload via curl. Payload: {payload}. Body: {stdout}", ex);
        }
    }

    private static async Task<string> SendPostWithCurl(
        string url,
        string payload,
        string operation,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "curl.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-sS");
        startInfo.ArgumentList.Add("-k");
        startInfo.ArgumentList.Add("-X");
        startInfo.ArgumentList.Add("POST");
        startInfo.ArgumentList.Add(url);
        startInfo.ArgumentList.Add("-H");
        startInfo.ArgumentList.Add("Accept: */*");
        startInfo.ArgumentList.Add("-H");
        startInfo.ArgumentList.Add("Content-Type: application/json");
        startInfo.ArgumentList.Add("-A");
        startInfo.ArgumentList.Add("curl/8.0.0");
        startInfo.ArgumentList.Add("-d");
        startInfo.ArgumentList.Add(payload);

        using var process = Process.Start(startInfo)
            ?? throw new Exception($"Nao foi possivel iniciar curl.exe para {operation}.");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
            throw new Exception($"Shopee {operation} falhou via curl. Payload: {payload}. ExitCode: {process.ExitCode}. Erro: {stderr}");

        return stdout;
    }

    private static async Task<string> SendGetWithCurl(
        string url,
        string operation,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "curl.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-sS");
        startInfo.ArgumentList.Add("-k");
        startInfo.ArgumentList.Add("-A");
        startInfo.ArgumentList.Add("curl/8.0.0");
        startInfo.ArgumentList.Add(url);

        using var process = Process.Start(startInfo)
            ?? throw new Exception($"Nao foi possivel iniciar curl.exe para {operation}.");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
            throw new Exception($"Shopee {operation} falhou via curl. ExitCode: {process.ExitCode}. Erro: {stderr}");

        return stdout;
    }

    private async Task<string> SendGetRequest(
        string path,
        string accessToken,
        int partnerId,
        string partnerKey,
        int shopId,
        string operation,
        IReadOnlyDictionary<string, string?> queryParams,
        CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sign = ShopeeSigner.Generate(partnerKey, $"{partnerId}{path}{timestamp}{accessToken}{shopId}");
        var baseUrl = BuildSignedUrl(path, partnerId, timestamp, sign, accessToken, shopId);
        var extraQuery = string.Join(
            "&",
            queryParams
                .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value!)}"));
        var url = string.IsNullOrWhiteSpace(extraQuery)
            ? baseUrl
            : $"{baseUrl}&{extraQuery}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if ((int)response.StatusCode == 405)
            return await SendGetWithCurl(url, operation, cancellationToken);

        if (!response.IsSuccessStatusCode
            && PareceErroHtmlInfraestrutura(body))
        {
            return await SendGetWithCurl(url, operation, cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Shopee {operation} falhou. Status: {(int)response.StatusCode}. Url: {url}. Resposta: {body}");

        return body;
    }

    private static void ValidarRespostaApi(JsonElement root, string body, string operation)
    {
        if (root.TryGetProperty("error", out var errorElement))
        {
            var error = errorElement.GetString();

            if (!string.IsNullOrWhiteSpace(error))
                throw new Exception($"Shopee {operation} rejeitou a resposta. Body: {body}");
        }
    }

    private static ShopeeOrderDetail ParseOrderDetail(JsonElement order)
    {
        var detalhe = new ShopeeOrderDetail
        {
            OrderSn = GetString(order, "order_sn") ?? string.Empty,
            CreateTime = GetDateTime(order, "create_time"),
            UpdateTime = GetDateTime(order, "update_time"),
            OrderStatus = GetString(order, "order_status"),
            BuyerUserId = GetString(order, "buyer_user_id"),
            TotalAmount = GetDecimal(order, "total_amount"),
            EstimatedShippingFee = GetDecimal(order, "estimated_shipping_fee"),
            ActualShippingFee = GetDecimal(order, "actual_shipping_fee")
        };

        if (order.TryGetProperty("recipient_address", out var recipient))
        {
            detalhe.Recipient = new ShopeeRecipientAddress
            {
                Name = GetString(recipient, "name"),
                Phone = GetString(recipient, "phone"),
                Town = GetString(recipient, "town"),
                District = GetString(recipient, "district"),
                City = GetString(recipient, "city"),
                State = GetString(recipient, "state"),
                ZipCode = GetString(recipient, "zipcode"),
                FullAddress = GetString(recipient, "full_address")
            };
        }

        if (order.TryGetProperty("package_list", out var packages))
        {
            foreach (var package in packages.EnumerateArray())
            {
                detalhe.PackageList.Add(new ShopeeOrderPackage
                {
                    ShippingCarrier = GetString(package, "shipping_carrier"),
                    TrackingNumber = GetString(package, "tracking_number")
                });
            }
        }

        if (order.TryGetProperty("item_list", out var itemList))
        {
            foreach (var item in itemList.EnumerateArray())
            {
                var quantidade = GetInt(item, "model_quantity_purchased")
                    ?? GetInt(item, "quantity_purchased")
                    ?? 0;
                var precoUnitario = GetDecimal(item, "model_discounted_price")
                    ?? GetDecimal(item, "model_original_price")
                    ?? GetDecimal(item, "item_price")
                    ?? 0m;
                var precoOriginal = GetDecimal(item, "model_original_price")
                    ?? GetDecimal(item, "original_price")
                    ?? precoUnitario;
                var desconto = Math.Max(0m, precoOriginal - precoUnitario);

                detalhe.ItemList.Add(new ShopeeOrderItem
                {
                    ItemId = GetString(item, "item_id"),
                    ModelId = GetString(item, "model_id"),
                    ItemSku = GetString(item, "item_sku"),
                    ModelSku = GetString(item, "model_sku"),
                    ItemName = GetString(item, "item_name"),
                    Quantity = quantidade,
                    Price = precoUnitario,
                    Discount = desconto > 0m ? desconto : null
                });
            }
        }

        return detalhe;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static DateTime? GetDateTime(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var unix))
            return DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;

        if (property.ValueKind == JsonValueKind.String && DateTime.TryParse(property.GetString(), out var parsed))
            return parsed;

        return null;
    }

    private static decimal? GetDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var valor))
            return valor;

        if (property.ValueKind == JsonValueKind.String
            && decimal.TryParse(property.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var valor))
            return valor;

        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out var parsed))
            return parsed;

        return null;
    }

    private static bool PareceErroHtmlInfraestrutura(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return false;

        return body.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || body.Contains("GitHub Pages", StringComparison.OrdinalIgnoreCase)
            || body.Contains("Site not found", StringComparison.OrdinalIgnoreCase);
    }

    private string BuildSignedUrl(
        string path,
        int partnerId,
        long timestamp,
        string sign,
        string accessToken,
        int shopId)
    {
        return
            $"{_config.BaseUrl.TrimEnd('/')}{path}" +
            $"?partner_id={partnerId}" +
            $"&timestamp={timestamp}" +
            $"&access_token={accessToken}" +
            $"&shop_id={shopId}" +
            $"&sign={sign}";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}

public class ShopeeCreateItemRequest
{
    [JsonPropertyName("item_name")]
    public string ItemName { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("item_sku")]
    public string? ItemSku { get; set; }

    [JsonPropertyName("gtin_code")]
    public string? GtinCode { get; set; }

    [JsonPropertyName("ncm")]
    public string? Ncm { get; set; }

    [JsonPropertyName("origin")]
    public string? Origin { get; set; }

    [JsonPropertyName("category_id")]
    public long CategoryId { get; set; }

    [JsonPropertyName("image")]
    public ShopeeImageRequest Image { get; set; } = new();

    [JsonPropertyName("weight")]
    public decimal Weight { get; set; }

    [JsonPropertyName("dimension")]
    public ShopeeDimensionRequest Dimension { get; set; } = new();

    [JsonPropertyName("price")]
    public decimal? Price { get; set; }

    [JsonPropertyName("original_price")]
    public decimal? OriginalPrice { get; set; }

    [JsonPropertyName("stock")]
    public int? Stock { get; set; }

    [JsonPropertyName("seller_stock")]
    public List<ShopeeSellerStockRequest> SellerStock { get; set; } = [];

    [JsonPropertyName("logistic_info")]
    public List<ShopeeLogisticInfoRequest> LogisticInfo { get; set; } = [];

    [JsonPropertyName("brand")]
    public ShopeeBrandRequest? Brand { get; set; }

    [JsonPropertyName("attribute_list")]
    public List<ShopeeAttributeRequest> AttributeList { get; set; } = [];
}

public class ShopeeImageRequest
{
    [JsonPropertyName("image_id_list")]
    public List<string> ImageIdList { get; set; } = [];
}

public class ShopeeDimensionRequest
{
    [JsonPropertyName("package_length")]
    public decimal PackageLength { get; set; }

    [JsonPropertyName("package_width")]
    public decimal PackageWidth { get; set; }

    [JsonPropertyName("package_height")]
    public decimal PackageHeight { get; set; }
}

public class ShopeeBrandRequest
{
    [JsonPropertyName("brand_id")]
    public long? BrandId { get; set; }

    [JsonPropertyName("original_brand_name")]
    public string? OriginalBrandName { get; set; }
}

public class ShopeeLogisticInfoRequest
{
    [JsonPropertyName("logistic_id")]
    public long LogisticId { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}

public class ShopeeSellerStockRequest
{
    [JsonPropertyName("location_id")]
    public string LocationId { get; set; } = "BRZ";

    [JsonPropertyName("stock")]
    public int Stock { get; set; }
}

public class ShopeeLogisticsChannel
{
    [JsonPropertyName("logistics_channel_id")]
    public long LogisticsChannelId { get; set; }

    [JsonPropertyName("logistics_channel_name")]
    public string LogisticsChannelName { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("logistics_capability")]
    public ShopeeLogisticsCapability? LogisticsCapability { get; set; }
}

public class ShopeeLogisticsCapability
{
    [JsonPropertyName("seller_logistics")]
    public bool SellerLogistics { get; set; }
}

public class ShopeeAttributeRequest
{
    [JsonPropertyName("attribute_id")]
    public long AttributeId { get; set; }

    [JsonPropertyName("attribute_value_list")]
    public List<ShopeeAttributeValueRequest> AttributeValueList { get; set; } = [];
}

public class ShopeeAttributeValueRequest
{
    [JsonPropertyName("value_id")]
    public long? ValueId { get; set; }

    [JsonPropertyName("original_value_name")]
    public string? OriginalValueName { get; set; }
}

public class ShopeeUpdateStockRequest
{
    [JsonPropertyName("item_id")]
    public long ItemId { get; set; }

    [JsonPropertyName("stock")]
    public int Stock { get; set; }

    [JsonPropertyName("seller_stock")]
    public List<ShopeeSellerStockRequest> SellerStock { get; set; } = [];
}

public class ShopeeUpdatePriceRequest
{
    [JsonPropertyName("item_id")]
    public long ItemId { get; set; }

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("original_price")]
    public decimal OriginalPrice { get; set; }
}

public class ShopeeUpdateItemRequest
{
    [JsonPropertyName("item_id")]
    public long ItemId { get; set; }

    [JsonPropertyName("item_name")]
    public string ItemName { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("item_sku")]
    public string? ItemSku { get; set; }

    [JsonPropertyName("gtin_code")]
    public string? GtinCode { get; set; }

    [JsonPropertyName("ncm")]
    public string? Ncm { get; set; }

    [JsonPropertyName("origin")]
    public string? Origin { get; set; }

    [JsonPropertyName("weight")]
    public decimal Weight { get; set; }

    [JsonPropertyName("dimension")]
    public ShopeeDimensionRequest Dimension { get; set; } = new();

    [JsonPropertyName("brand")]
    public ShopeeBrandRequest? Brand { get; set; }

    [JsonPropertyName("attribute_list")]
    public List<ShopeeAttributeRequest> AttributeList { get; set; } = [];
}

public class ShopeeDeleteItemRequest
{
    [JsonPropertyName("item_id")]
    public long ItemId { get; set; }
}

public class ShopeeOrderSummary
{
    public string OrderSn { get; set; } = "";
    public DateTime? CreateTime { get; set; }
    public DateTime? UpdateTime { get; set; }
    public string? OrderStatus { get; set; }
}

public class ShopeeOrderDetail
{
    public string OrderSn { get; set; } = "";
    public DateTime? CreateTime { get; set; }
    public DateTime? UpdateTime { get; set; }
    public string? OrderStatus { get; set; }
    public string? BuyerUserId { get; set; }
    public decimal? TotalAmount { get; set; }
    public decimal? EstimatedShippingFee { get; set; }
    public decimal? ActualShippingFee { get; set; }
    public ShopeeRecipientAddress? Recipient { get; set; }
    public List<ShopeeOrderPackage> PackageList { get; set; } = [];
    public List<ShopeeOrderItem> ItemList { get; set; } = [];
}

public class ShopeeRecipientAddress
{
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? Town { get; set; }
    public string? District { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
    public string? FullAddress { get; set; }
}

public class ShopeeOrderPackage
{
    public string? ShippingCarrier { get; set; }
    public string? TrackingNumber { get; set; }
}

public class ShopeeOrderItem
{
    public string? ItemId { get; set; }
    public string? ModelId { get; set; }
    public string? ItemSku { get; set; }
    public string? ModelSku { get; set; }
    public string? ItemName { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal? Discount { get; set; }
}
