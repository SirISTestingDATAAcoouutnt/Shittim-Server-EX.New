using Schale.FlatData;
using Shittim.Services.Client;
using Schale.Data.GameModel;
using Microsoft.EntityFrameworkCore;

namespace Shittim.Commands
{
    // ALTERADO: O gatilho mudou para "bp" e a rota de uso foi limpa para "/bp"
    [CommandHandler("bp", "Command to unlock Battle Pass paid track (keep level)", "/bp")]
    internal class UnlockBattlePassCommand : Command
    {
        public UnlockBattlePassCommand(IClientConnection connection, string[] args, bool validate = true) : base(connection, args, validate) { }

        public override async Task Execute()
        {
            using var context = await connection.Context.CreateDbContextAsync();
            var account = context.GetAccount(connection.AccountServerId);

            var battlePassExcel = connection.ExcelTableService.GetTable<BattlePassInfoExcelT>();

            foreach (var season in battlePassExcel)
            {
                var bp = await context.BattlePasses.FirstOrDefaultAsync(x => x.AccountServerId == account.ServerId && x.BattlePassId == season.Id);
                if (bp == null)
                {
                    bp = new BattlePassDBServer
                    {
                        AccountServerId = account.ServerId,
                        BattlePassId = season.Id,
                        PassLevel = 1,
                        PassExp = 0,
                        PurchaseGroupId = 0,
                        ReceiveRewardLevel = 0,
                        ReceivePurchaseRewardLevel = 0,
                        WeeklyPassExp = 0,
                        LastWeeklyPassExpLimitRefreshDate = DateTime.Now
                    };
                    context.BattlePasses.Add(bp);
                }
                
                if (bp.PurchaseGroupId == 0)
                {
                    bp.PurchaseGroupId = 1; 
                }
            }

            await context.SaveChangesAsync();
            await connection.SendChatMessage("Unlocked Battle Pass Paid Track (Grind Mode)!");
        }
    }
}
