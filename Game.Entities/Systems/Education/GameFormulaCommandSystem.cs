using Unity.Entities;
using Unity.Mathematics;

public struct GameFormulaCommandsDefinition
{
    public struct Command
    {
        public struct Formula
        {
            public int index;
            public float chance;
        }

        public float min;
        public float max;

        public BlobArray<Formula> formulas;

        public void Execute<T>(
            int type, 
            in T formulaManager, 
            in DynamicBuffer<GameFormula> formulas, 
            ref DynamicBuffer<GameFormulaCommand> formulaCommands, 
            ref Random random) where T : IGameFormulaManager
        {
            GameFormulaCommand formulaCommand;
            float count, chance = random.NextFloat();
            int numFormulas = formulas.Length;
            for(int i = 0; i < numFormulas; ++i)
            {
                ref var formula = ref this.formulas[i];

                formulaCommand.count = formulaManager.GetRemainingCount(type, formula.index, formulas);
                if (formulaCommand.count < 1)
                    continue;

                if (formula.chance < chance)
                {
                    chance -= formula.chance;

                    continue;
                }

                count = math.min(formulaCommand.count, max * formula.chance);
                count = random.NextFloat(math.clamp(min * formula.chance, 1.0f, count), count);

                formulaCommand.count = (int)math.round(count);
                formulaCommand.index = formula.index;

                formulaCommands.Add(formulaCommand);

                break;
            }
        }
    }

    public BlobArray<Command> commands;
}

public partial class GameFormulaCommandSystem
{
}
