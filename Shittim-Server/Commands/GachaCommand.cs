using Shittim.Services.Client;
using Schale.Excel;
using Schale.FlatData;
using System.Text;
using System.Text.Json;

namespace Shittim.Commands
{
    [CommandHandler("gc", "Command to set gacha rates and guarantee", "/gc [type] [value]")]
    internal class GachaCommand : Command
    {
        public GachaCommand(IClientConnection connection, string[] args, bool validate = true) : base(connection, args, validate) { }

        [Argument(0, @"^rate$|^guarantee$|^reset$|^settings$|^show$|^help$", "Operation type", ArgumentFlags.IgnoreCase | ArgumentFlags.Optional)]
        public string Type { get; set; } = string.Empty;

        [Argument(1, @"^.*$", "Value (pickup character ID or rarity r3/r2/r1)", ArgumentFlags.IgnoreCase | ArgumentFlags.Optional)]
        public string Value { get; set; } = string.Empty;

        [Argument(2, @"^.*$", "Rate percentage", ArgumentFlags.Optional)]
        public string Rate { get; set; } = string.Empty;

        private static string ConfigPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "gacha_config.json");

        private class GachaConfig
        {
            public Dictionary<string, double> custom_rates { get; set; } = new();
            public long? guaranteed_character { get; set; }
        }

        public override async Task Execute()
        {
            string operation = (Type ?? string.Empty).ToLower().Trim();

            if (string.IsNullOrEmpty(operation) || operation == "help")
            {
                await ShowHelp();
                return;
            }

            switch (operation)
            {
                case "rate":
                    if (string.IsNullOrEmpty(Value) || string.IsNullOrEmpty(Rate))
                    {
                        await connection.SendChatMessage("Usage: /gc rate [r3/r2/r1] [percentage]");
                        return;
                    }
                    await SetRate(Value, Rate);
                    break;

                case "guarantee":
                    if (string.IsNullOrEmpty(Value))
                    {
                        await connection.SendChatMessage("Usage: /gc guarantee [characterId]");
                        return;
                    }
                    await SetGuarantee(Value);
                    break;

                case "show":
                    await ListAvailableCharacters();
                    break;

                case "reset":
                    await ResetGachaSettings();
                    break;

                case "settings":
                    await ShowCurrentSettings();
                    break;

                default:
                    await connection.SendChatMessage("Unknown operation! Try /gc help");
                    break;
            }
        }

        private GachaConfig LoadConfig()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return new GachaConfig();
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<GachaConfig>(json) ?? new GachaConfig();
            }
            catch
            {
                return new GachaConfig();
            }
        }

        private void SaveConfig(GachaConfig config)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(config, options);
                File.WriteAllText(ConfigPath, json);
                
                Shittim_Server.Core.GachaCommand.ClearCache();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GachaCommand] Failed to save JSON config: {ex.Message}");
            }
        }

        private async Task SetRate(string rarity, string rateStr)
        {
            if (!double.TryParse(rateStr, out double rate) || rate < 0 || rate > 100)
            {
                await connection.SendChatMessage("Invalid rate! Please use a number between 0 and 100.");
                return;
            }

            var config = LoadConfig();
            config.custom_rates ??= new Dictionary<string, double>();

            switch (rarity.ToLower())
            {
                case "r3":
                    config.custom_rates["ssr"] = rate;
                    await connection.SendChatMessage($"Raridade R3 (SSR) configurada para {rate}% no JSON!");
                    break;
                case "r2":
                    config.custom_rates["sr"] = rate;
                    await connection.SendChatMessage($"Raridade R2 (SR) configurada para {rate}% no JSON!");
                    break;
                case "r1":
                    config.custom_rates["r"] = rate;
                    await connection.SendChatMessage($"Raridade R1 (R) configurada para {rate}% no JSON!");
                    break;
                default:
                    await connection.SendChatMessage("Invalid rarity! Use r3, r2, or r1.");
                    return;
            }

            SaveConfig(config);
        }

        private async Task SetGuarantee(string characterId)
        {
            if (!long.TryParse(characterId, out long id))
            {
                await connection.SendChatMessage("Invalid character ID!");
                return;
            }

            var characterExcel = connection.ExcelTableService.GetTable<CharacterExcelT>();
            var character = characterExcel.FirstOrDefault(x => x.Id == id);

            if (character == null)
            {
                await connection.SendChatMessage("Character not found!");
                return;
            }

            var config = LoadConfig();
            config.guaranteed_character = id;
            SaveConfig(config);

            await connection.SendChatMessage($"Guaranteed character set to ID {id} in JSON!");
        }

        private async Task ResetGachaSettings()
        {
            var config = new GachaConfig();
            SaveConfig(config);
            await connection.SendChatMessage("Gacha JSON settings completely reset to default!");
        }

        private async Task ShowCurrentSettings()
        {
            var config = LoadConfig();
            await connection.SendChatMessage("Current Gacha Config File Settings:");

            if (config.custom_rates != null && config.custom_rates.Count > 0)
            {
                foreach (var rate in config.custom_rates)
                {
                    string rarityName = rate.Key switch
                    {
                        "ssr" => "R3 (SSR)",
                        "sr" => "R2 (SR)",
                        "r" => "R1 (R)",
                        _ => rate.Key.ToUpper()
                    };
                    await connection.SendChatMessage($"{rarityName} Rate: {rate.Value}%");
                }
            }
            else
            {
                await connection.SendChatMessage("Rates: Using Default Server Rates");
            }

            if (config.guaranteed_character.HasValue && config.guaranteed_character.Value > 0)
                await connection.SendChatMessage($"Guaranteed Character ID: {config.guaranteed_character.Value}");
            else
                await connection.SendChatMessage("No guaranteed character set");
        }

        private async Task ListAvailableCharacters()
        {
            var characterExcel = connection.ExcelTableService.GetTable<CharacterExcelT>();
            var characters = characterExcel.GetReleaseCharacters().ToList();

            if (!characters.Any())
            {
                await connection.SendChatMessage("No characters found!");
                return;
            }

            await connection.SendChatMessage("Available Characters:");
            var currentRarity = -1;
            var sb = new StringBuilder();

            foreach (var character in characters)
            {
                if (currentRarity != character.DefaultStarGrade)
                {
                    if (sb.Length > 0)
                    {
                        await connection.SendChatMessage(sb.ToString());
                        sb.Clear();
                    }
                    
                    string rarity = character.DefaultStarGrade switch
                    {
                        3 => "R3 (3★)",
                        2 => "R2 (2★)",
                        1 => "R1 (1★)",
                        _ => $"{character.DefaultStarGrade}★"
                    };
                    await connection.SendChatMessage($"\n{rarity} Characters:");
                    currentRarity = character.DefaultStarGrade;
                }

                sb.Append($"[{character.Id}] {character.DevName}, ");

                if (sb.Length > 200)
                {
                    await connection.SendChatMessage(sb.ToString().TrimEnd(',', ' '));
                    sb.Clear();
                }
            }

            if (sb.Length > 0)
                await connection.SendChatMessage(sb.ToString().TrimEnd(',', ' '));
        }

        private async Task ShowHelp()
        {
            await connection.SendChatMessage("/gc - Command to set gacha rates and guarantee");
            await connection.SendChatMessage("Usage: /gc [type] [value] [rate]");
            await connection.SendChatMessage("Types:");
            await connection.SendChatMessage("rate [r3/r2/r1] [percentage] - Set rate for rarity");
            await connection.SendChatMessage("guarantee [characterId] - Set guaranteed character ID");
            await connection.SendChatMessage("/gc show - Show list of available Characters");
            await connection.SendChatMessage("/gc reset - Reset to default JSON settings");
            await connection.SendChatMessage("/gc settings - Show current configuration");
        }
    }
}
