using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using GammaLibrary.Extensions;
using Settings;
using WarframeAlertingPrime.SDK.Models.Core;
using WarframeAlertingPrime.SDK.Models.Enums;
using WarframeAlertingPrime.SDK.Models.Others;

namespace TRKS.WF.QQBot
{
    public class RMSearcher
    {
        private Timer timer = new Timer(TimeSpan.FromHours(2).TotalMilliseconds);
        private WFTranslator translator => WFResource.WFTranslator;

        private bool isWFA = !Config.Instance.ClientId.IsNullOrWhiteSpace() &&
                             !Config.Instance.ClientSecret.IsNullOrWhiteSpace();

        private string platform = Config.Instance.Platform.ToString();

        private Client wfaClient;

        public RMSearcher()
        {
            // UpdateAccessToken();
            UpdateClient();
            timer.Elapsed += (s, e) => /*UpdateAccessToken();*/ UpdateClient();
            timer.Start();
        }

        public void UpdateClient()
        {
            PlatformType wfaPlatform;
            switch (Config.Instance.Platform)
            {
                case Platform.PC:
                    wfaPlatform = PlatformType.PC;
                    break;
                case Platform.NS:
                    wfaPlatform = PlatformType.Switch;
                    break;
                case Platform.PS4:
                    wfaPlatform = PlatformType.PS4;
                    break;
                case Platform.XBOX:
                    wfaPlatform = PlatformType.Xbox;
                    break;
                default:
                    wfaPlatform = PlatformType.PC;
                    break;
            }

            if (isWFA) // 今后所有用到client的地方都要判断一次
            {
                if (DateTime.Now - Config.Instance.Last_update > TimeSpan.FromDays(7))
                {
                    wfaClient = new Client(Config.Instance.ClientId, Config.Instance.ClientSecret, new []
                    {
                        "wfa.basic", "wfa.riven.query", "wfa.user.read", "wfa.lib.query"

                    }, wfaPlatform);
                    wfaClient.InitAsync();
                    Config.Instance.Last_update = DateTime.Now;
                    Config.Instance.AcessToken = wfaClient.Token;
                    Config.Save();
                }
                else
                {
                    wfaClient = new Client(Config.Instance.AcessToken, wfaPlatform);
                }
            }

        }
        public string GetAccessToken()
        {
            var body = $"client_id={Config.Instance.ClientId}&client_secret={Config.Instance.ClientSecret}&grant_type=client_credentials";
            var header = new WebHeaderCollection
            {
                { "Content-Type", "application/x-www-form-urlencoded" }
            };
            var accessToken = WebHelper.UploadJson<AccessToken>("https://api.richasy.cn/connect/token", body, header).access_token;

            Config.Instance.Last_update = DateTime.Now;
            Config.Save();
            return accessToken;
        }

        public void UpdateAccessToken()
        {
            if (isWFA && DateTime.Now - Config.Instance.Last_update > TimeSpan.FromDays(7))
            {
                Config.Instance.AcessToken = GetAccessToken();
                Config.Save();
            }
        }

        /*public List<RivenInfo> GetRivenInfos(string weapon)
        {
            var header = new WebHeaderCollection();
            var count = Config.Instance.WFASearchCount;
            var platform = Config.Instance.Platform.GetSymbols().First();
            if (Config.Instance.Platform == Platform.NS)
            {
                platform = "ns";
            }
            header.Add("Authorization", $"Bearer {Config.Instance.AcessToken}");
            header.Add("Platform", platform);
            header.Add("Weapon", weapon.ToBase64());
            return WebHelper.DownloadJson<List<RivenInfo>>($"https://api.richasy.cn/wfa/rm/riven", header).Where(info => info.isSell == 1).Take(count).ToList(); // 操 云之幻好蠢 为什么不能在请求里限制是买还是卖
        }*/

        public List<WarframeAlertingPrime.SDK.Models.User.Order> GetRivenOrders(string weapon)
        {
            var option = new SearchRivenOrderOption{Category = "", IsVeiled = false, OrderType = "sell", Page = 1, PageSize = 20, Weapon = Uri.EscapeUriString(weapon)};
            var orders = wfaClient.QueryRivenOrdersAsync(option).Result;
            translator.TranslateRivenOrders(orders);
            return orders;
        }

        public List<RivenData> GetRivenDatas()
        {
            var info = WebHelper.DownloadJson<List<RivenData>>(
                "http://n9e5v4d8.ssl.hwcdn.net/repos/weeklyRivensPC.json");
            info.ForEach(d => d.compatibility = d.compatibility.IsNullOrEmpty() ? "" : translator.TranslateWeapon(d.compatibility.Replace("<ARCHWING> ", "").Format()));
            return info;
        }
        public void SendRivenInfos(GroupNumber group, string weapon)
        {
            var sb = new StringBuilder();
            try
            {
                if (isWFA)
                {
                    var weaponEn = translator.TranslateWeapon(weapon);
                    if (weaponEn != weapon)
                    {
                        Messenger.SendGroup(group, "好嘞, 等着, 着啥急啊, 这不帮你查呢.");
                        var orders = GetRivenOrders(weaponEn);
                        var data = GetRivenDatas().Where(d => d.compatibility.Format() == weapon).ToList();
                        var msg = orders.Any() ? WFFormatter.ToString(orders, data) : $"抱歉, 目前紫卡市场没有任何出售: {weapon} 紫卡的用户.".AddRemainCallCount(group);
                        sb.AppendLine(msg.AddPlatformInfo());
                    }
                    else
                    {
                        sb.AppendLine($"武器 {weapon} 不存在.");
                        var similarlist = translator.GetSimilarItem(weapon, "rm");
                        if (similarlist.Any())
                        {
                            sb.AppendLine("请问这下面有没有你要找的武器呢?（可尝试复制下面的名称来进行搜索)");
                            foreach (var item in similarlist)
                            {
                                sb.AppendLine($"    {item}");
                            }
                        }

                    }
                }
                else
                {
                    sb.AppendLine("本机器人没有 WFA 授权, 本功能无法使用, 请联系机器人管理员.");
                }
            }
            catch (WebException)
            {
                sb.AppendLine("经过我们的多次尝试, 依然无法访问紫卡市场. 如果你不能谅解, 有本事顺着网线来打我呀.");
            }
            Messenger.SendGroup(group, sb.ToString().Trim());
        }
    }
}
