using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public class EnhancedLR0Parser
{
    private const string EPSILON = "epsilon";
    private const string END_MARKER = "$";
    private const string AUGMENTED_SUFFIX = "'";

    #region Data Structures

    public class Production
    {
        public string LHS { get; }
        public List<string> RHS { get; }

        public Production(string lhs, List<string> rhs)
        {
            LHS = lhs ?? throw new ArgumentNullException(nameof(lhs));
            RHS = rhs ?? throw new ArgumentNullException(nameof(rhs));
        }

        public override string ToString() => $"{LHS} -> {string.Join(" ", RHS)}";
        
        public override bool Equals(object obj)
        {
            return obj is Production other && 
                   LHS == other.LHS && 
                   RHS.SequenceEqual(other.RHS);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(LHS, string.Join(" ", RHS));
        }
    }

    public class Item
    {
        public string LHS { get; }
        public List<string> RHS { get; }
        public int Dot { get; }

        public Item(string lhs, List<string> rhs, int dot)
        {
            LHS = lhs ?? throw new ArgumentNullException(nameof(lhs));
            RHS = rhs ?? throw new ArgumentNullException(nameof(rhs));
            
            if (dot < 0 || dot > rhs.Count)
                throw new ArgumentOutOfRangeException(nameof(dot), "Dot position is out of range");
                
            Dot = dot;
        }

        public bool IsComplete => Dot >= RHS.Count;
        public bool CanAdvance => Dot < RHS.Count;
        public string NextSymbol => CanAdvance ? RHS[Dot] : null;

        public override bool Equals(object obj)
        {
            return obj is Item other && 
                   LHS == other.LHS && 
                   Dot == other.Dot && 
                   RHS.SequenceEqual(other.RHS);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(LHS, string.Join(" ", RHS), Dot);
        }

        public override string ToString()
        {
            var sb = new StringBuilder($"{LHS} -> ");
            for (int i = 0; i < RHS.Count; i++)
            {
                if (i == Dot) sb.Append("• ");
                sb.Append($"{RHS[i]} ");
            }
            if (Dot == RHS.Count) sb.Append("•");
            return sb.ToString().Trim();
        }
    }

    public class ParserException : Exception
    {
        public ParserException(string message) : base(message) { }
        public ParserException(string message, Exception innerException) : base(message, innerException) { }
    }

    #endregion

    #region Fields

    private readonly List<Production> grammar = new();
    private readonly HashSet<string> terminals = new();
    private readonly HashSet<string> nonTerminals = new();
    private readonly Dictionary<string, HashSet<string>> first = new();
    private readonly Dictionary<string, HashSet<string>> follow = new();
    
    private string startSymbol;
    private readonly List<HashSet<Item>> states = new();
    private readonly Dictionary<(int state, string symbol), int> transitions = new();
    private readonly Dictionary<(int state, string symbol), string> actionTable = new();
    private readonly Dictionary<(int state, string symbol), int> gotoTable = new();

    #endregion

    #region Public Methods

    public void LoadGrammar(List<(string lhs, List<List<string>> rhsList)> grammarRules)
    {
        if (grammarRules == null || !grammarRules.Any())
            throw new ArgumentException("Grammar rules cannot be null or empty");

        Clear();

        foreach (var (lhs, rhsList) in grammarRules)
        {
            if (string.IsNullOrWhiteSpace(lhs))
                throw new ArgumentException($"Invalid LHS: '{lhs}'");

            nonTerminals.Add(lhs);
            
            foreach (var rhs in rhsList)
            {
                if (rhs == null)
                    throw new ArgumentException($"RHS cannot be null for production {lhs}");

                // --- NEW CODE ---
                // Filter out the "epsilon" keyword to create a truly empty RHS for epsilon-productions.
                var cleanRhs = rhs.Where(s => !string.IsNullOrWhiteSpace(s) && s != EPSILON).ToList();
                grammar.Add(new Production(lhs, cleanRhs));

                // Identify terminals
                foreach (var symbol in cleanRhs)
                {
                    if (symbol != EPSILON && !IsNonTerminal(symbol))
                        terminals.Add(symbol);
                }
            }
        }

        if (!grammar.Any())
            throw new ArgumentException("No valid productions found in grammar");

        startSymbol = grammar[0].LHS;
        terminals.Add(END_MARKER);

        ValidateGrammar();
    }

    public void BuildParser()
    {
        try
        {
            if (!grammar.Any())
                throw new InvalidOperationException("Grammar not loaded. Call LoadGrammar first.");

            ComputeFirstSets();
            ComputeFollowSets();
            BuildLR0Automaton();
            BuildParsingTables();
            
            Console.WriteLine("Parser built successfully!");
        }
        catch (Exception ex)
        {
            throw new ParserException($"Failed to build parser: {ex.Message}", ex);
        }
    }

    public bool ParseInput(List<string> inputTokens)
    {
        if (inputTokens == null)
            throw new ArgumentNullException(nameof(inputTokens));

        if (!inputTokens.Any() || inputTokens.Last() != END_MARKER)
            inputTokens.Add(END_MARKER);

        return SimulateParser(inputTokens);
    }

    public void PrintGrammarInfo()
    {
        PrintProductions();
        PrintFirstSets();
        PrintFollowSets();
        PrintStates();
        PrintParsingTables();
    }

    #endregion

    #region Grammar Input and Validation

    public void InputGrammarInteractively()
    {
        Console.WriteLine("Enter grammar rules (format: A -> a B | b)");
        Console.WriteLine("Type 'end' to finish:");

        var grammarRules = new List<(string lhs, List<List<string>> rhsList)>();
        string line;

        while (!string.IsNullOrEmpty(line = Console.ReadLine()) && line.Trim() != "end")
        {
            try
            {
                var (lhs, rhsList) = ParseGrammarLine(line);
                grammarRules.Add((lhs, rhsList));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing line '{line}': {ex.Message}");
                Console.WriteLine("Please try again.");
            }
        }

        LoadGrammar(grammarRules);
    }

    private (string lhs, List<List<string>> rhsList) ParseGrammarLine(string line)
    {
        var parts = line.Split(new[] { "->" }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            throw new ArgumentException("Invalid grammar format. Expected: A -> a B | b");

        string lhs = parts[0].Trim();
        if (string.IsNullOrWhiteSpace(lhs))
            throw new ArgumentException("Left-hand side cannot be empty");

        var rightParts = parts[1].Split('|');
        var rhsList = new List<List<string>>();

        foreach (var part in rightParts)
        {
            var symbols = part.Trim()
                             .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                             .ToList();
            
            // --- NEW CODE ---
            // If symbols are empty, it already represents an epsilon production.
            // The new LoadGrammar logic handles this correctly, so no explicit "epsilon" string is needed.
            if (!symbols.Any() && part.Trim().Length == 0)
            {
                // Add an empty list to signify an epsilon production.
                rhsList.Add(new List<string>());
            }
            else
            {
                rhsList.Add(symbols);
            }
        }

        return (lhs, rhsList);
    }

    private void ValidateGrammar()
    {
        // Check for unreachable symbols
        var reachable = new HashSet<string> { startSymbol };
        bool changed;
        
        do
        {
            changed = false;
            foreach (var prod in grammar)
            {
                if (reachable.Contains(prod.LHS))
                {
                    foreach (var symbol in prod.RHS)
                    {
                        if (symbol != EPSILON && reachable.Add(symbol))
                            changed = true;
                    }
                }
            }
        } while (changed);

        var unreachableNonTerminals = nonTerminals.Except(reachable).ToList();
        if (unreachableNonTerminals.Any())
        {
            Console.WriteLine($"Warning: Unreachable non-terminals found: {string.Join(", ", unreachableNonTerminals)}");
        }

        // Check for undefined non-terminals
        var definedNonTerminals = grammar.Select(p => p.LHS).ToHashSet();
        var undefinedNonTerminals = nonTerminals.Except(definedNonTerminals).ToList();
        if (undefinedNonTerminals.Any())
        {
            throw new ParserException($"Undefined non-terminals: {string.Join(", ", undefinedNonTerminals)}");
        }
    }

    #endregion

    #region First and Follow Set Computation

    private void ComputeFirstSets()
    {
        first.Clear();
        
        // Initialize
        foreach (var nt in nonTerminals)
            first[nt] = new HashSet<string>();

        bool changed;
        do
        {
            changed = false;
            foreach (var prod in grammar)
            {
                var firstRhs = ComputeFirstOfSequence(prod.RHS);
                int beforeCount = first[prod.LHS].Count;
                first[prod.LHS].UnionWith(firstRhs);
                if (first[prod.LHS].Count > beforeCount)
                    changed = true;
            }
        } while (changed);
    }

    private HashSet<string> ComputeFirstOfSequence(List<string> symbols)
    {
        var result = new HashSet<string>();
        
        if (!symbols.Any() || (symbols.Count == 1 && symbols[0] == EPSILON))
        {
            result.Add(EPSILON);
            return result;
        }

        foreach (var symbol in symbols)
        {
            if (terminals.Contains(symbol))
            {
                result.Add(symbol);
                break;
            }

            if (first.ContainsKey(symbol))
            {
                result.UnionWith(first[symbol].Where(s => s != EPSILON));
                if (!first[symbol].Contains(EPSILON))
                    break;
            }
            else
            {
                break; // Unknown symbol, can't continue
            }
        }

        // If all symbols derive epsilon, add epsilon to result
        if (symbols.All(s => terminals.Contains(s) ? s == EPSILON : 
                           first.ContainsKey(s) && first[s].Contains(EPSILON)))
        {
            result.Add(EPSILON);
        }

        return result;
    }

    private void ComputeFollowSets()
    {
        follow.Clear();
        follow[startSymbol] = new HashSet<string> { END_MARKER };

        bool changed;
        do
        {
            changed = false;
            foreach (var prod in grammar)
            {
                for (int i = 0; i < prod.RHS.Count; i++)
                {
                    string symbol = prod.RHS[i];
                    if (!nonTerminals.Contains(symbol)) continue;

                    var beta = prod.RHS.Skip(i + 1).ToList();
                    var firstBeta = ComputeFirstOfSequence(beta);

                    if (!follow.ContainsKey(symbol))
                        follow[symbol] = new HashSet<string>();

                    int beforeCount = follow[symbol].Count;
                    follow[symbol].UnionWith(firstBeta.Where(s => s != EPSILON));

                    if (firstBeta.Contains(EPSILON) || !beta.Any())
                    {
                        if (follow.ContainsKey(prod.LHS))
                            follow[symbol].UnionWith(follow[prod.LHS]);
                    }

                    if (follow[symbol].Count > beforeCount)
                        changed = true;
                }
            }
        } while (changed);
    }

    #endregion

    #region LR(0) Automaton Construction

    private void BuildLR0Automaton()
    {
        states.Clear();
        transitions.Clear();

        // Create augmented grammar
        string augmentedStart = startSymbol + AUGMENTED_SUFFIX;
        grammar.Insert(0, new Production(augmentedStart, new List<string> { startSymbol }));
        nonTerminals.Add(augmentedStart);

        // Create initial state
        var startItem = new Item(augmentedStart, new List<string> { startSymbol }, 0);
        var initialState = Closure(new HashSet<Item> { startItem });
        states.Add(initialState);

        var workQueue = new Queue<int>();
        workQueue.Enqueue(0);

        while (workQueue.Count > 0)
        {
            int currentStateIndex = workQueue.Dequeue();
            var currentState = states[currentStateIndex];

            var symbols = currentState
                .Where(item => item.CanAdvance)
                .Select(item => item.NextSymbol)
                .ToHashSet();

            foreach (var symbol in symbols)
            {
                var newState = Goto(currentState, symbol);
                if (!newState.Any()) continue;

                int targetStateIndex = FindOrCreateState(newState);
                transitions[(currentStateIndex, symbol)] = targetStateIndex;

                if (targetStateIndex == states.Count - 1) // New state created
                {
                    workQueue.Enqueue(targetStateIndex);
                }
            }
        }
    }

    private HashSet<Item> Closure(HashSet<Item> items)
    {
        var closure = new HashSet<Item>(items);
        var workQueue = new Queue<Item>(items);

        while (workQueue.Count > 0)
        {
            var item = workQueue.Dequeue();
            
            if (item.CanAdvance && nonTerminals.Contains(item.NextSymbol))
            {
                string nextSymbol = item.NextSymbol;
                foreach (var prod in grammar.Where(p => p.LHS == nextSymbol))
                {
                    var newItem = new Item(prod.LHS, prod.RHS, 0);
                    if (closure.Add(newItem))
                    {
                        workQueue.Enqueue(newItem);
                    }
                }
            }
        }

        return closure;
    }

    private HashSet<Item> Goto(HashSet<Item> items, string symbol)
    {
        var movedItems = new HashSet<Item>();
        
        foreach (var item in items)
        {
            if (item.CanAdvance && item.NextSymbol == symbol)
            {
                movedItems.Add(new Item(item.LHS, item.RHS, item.Dot + 1));
            }
        }

        return movedItems.Any() ? Closure(movedItems) : new HashSet<Item>();
    }

    private int FindOrCreateState(HashSet<Item> newState)
    {
        for (int i = 0; i < states.Count; i++)
        {
            if (states[i].SetEquals(newState))
                return i;
        }

        states.Add(newState);
        return states.Count - 1;
    }

    #endregion

    #region Parsing Table Construction

    private void BuildParsingTables()
    {
        actionTable.Clear();
        gotoTable.Clear();

        for (int i = 0; i < states.Count; i++)
        {
            foreach (var item in states[i])
            {
                if (item.CanAdvance)
                {
                    string nextSymbol = item.NextSymbol;
                    if (terminals.Contains(nextSymbol) && 
                        transitions.TryGetValue((i, nextSymbol), out int shiftTarget))
                    {
                        var key = (i, nextSymbol);
                        if (actionTable.ContainsKey(key))
                        {
                            throw new ParserException($"Shift/Reduce conflict at state {i}, symbol '{nextSymbol}'");
                        }
                        actionTable[key] = "s" + shiftTarget;
                    }
                }
                else // Complete item
                {
                    if (item.LHS == startSymbol + AUGMENTED_SUFFIX)
                    {
                        actionTable[(i, END_MARKER)] = "acc";
                    }
                    else
                    {
                        int prodIndex = FindProductionIndex(item.LHS, item.RHS);
                        if (prodIndex == -1)
                        {
                            throw new ParserException($"Production not found: {item}");
                        }

                        if (follow.TryGetValue(item.LHS, out var followSet))
                        {
                            foreach (var terminal in followSet)
                            {
                                var key = (i, terminal);
                                if (actionTable.ContainsKey(key))
                                {
                                    throw new ParserException($"Reduce/Reduce conflict at state {i}, terminal '{terminal}'");
                                }
                                actionTable[key] = "r" + prodIndex;
                            }
                        }
                    }
                }
            }

            // Build GOTO table
            foreach (var nonTerminal in nonTerminals)
            {
                if (transitions.TryGetValue((i, nonTerminal), out int gotoTarget))
                {
                    gotoTable[(i, nonTerminal)] = gotoTarget;
                }
            }
        }
    }

    private int FindProductionIndex(string lhs, List<string> rhs)
    {
        for (int i = 0; i < grammar.Count; i++)
        {
            if (grammar[i].LHS == lhs && grammar[i].RHS.SequenceEqual(rhs))
                return i;
        }
        return -1;
    }

    #endregion

    #region Parser Simulation

    private bool SimulateParser(List<string> input)
    {
        var stateStack = new Stack<int>();
        var symbolStack = new Stack<string>();
        stateStack.Push(0);

        int inputPointer = 0;
        var steps = new List<string>();

        Console.WriteLine("=== Parser Simulation ===");
        Console.WriteLine($"Input: {string.Join(" ", input)}");
        Console.WriteLine();

        while (inputPointer < input.Count)
        {
            int currentState = stateStack.Peek();
            string currentSymbol = input[inputPointer];

            if (!actionTable.TryGetValue((currentState, currentSymbol), out string action))
            {
                Console.WriteLine($"ERROR: No action for state {currentState}, symbol '{currentSymbol}'");
                Console.WriteLine($"Valid actions from state {currentState}:");
                
                foreach (var ((state, symbol), act) in actionTable)
                {
                    if (state == currentState)
                        Console.WriteLine($"  {symbol} -> {act}");
                }
                
                return false;
            }

            var step = $"State: {currentState}, Input: '{currentSymbol}', Action: {action}";
            steps.Add(step);
            Console.WriteLine(step);

            try
            {
                if (action.StartsWith("s")) // Shift
                {
                    int targetState = int.Parse(action.Substring(1));
                    symbolStack.Push(currentSymbol);
                    stateStack.Push(targetState);
                    inputPointer++;
                }
                else if (action.StartsWith("r")) // Reduce
                {
                    int prodIndex = int.Parse(action.Substring(1));
                    var production = grammar[prodIndex];
                    
                    // --- NEW CODE ---
                    // With an empty RHS for epsilon, the pop count is simply the number of symbols.
                    int popCount = production.RHS.Count;

                    for (int i = 0; i < popCount; i++)
                    {
                        if (symbolStack.Count > 0) symbolStack.Pop();
                        if (stateStack.Count > 0) stateStack.Pop();
                    }

                    symbolStack.Push(production.LHS);
                    
                    if (!gotoTable.TryGetValue((stateStack.Peek(), production.LHS), out int gotoState))
                    {
                        Console.WriteLine($"ERROR: No GOTO entry for state {stateStack.Peek()}, non-terminal '{production.LHS}'");
                        return false;
                    }
                    
                    stateStack.Push(gotoState);
                    Console.WriteLine($"  Reduced by: {production}");
                }
                else if (action == "acc") // Accept
                {
                    Console.WriteLine("SUCCESS: Input accepted!");
                    return true;
                }
                else
                {
                    Console.WriteLine($"ERROR: Unknown action '{action}'");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR during parsing: {ex.Message}");
                return false;
            }
        }

        Console.WriteLine("ERROR: Unexpected end of parsing");
        return false;
    }

    #endregion

    #region Helper Methods

    private void Clear()
    {
        grammar.Clear();
        terminals.Clear();
        nonTerminals.Clear();
        first.Clear();
        follow.Clear();
        states.Clear();
        transitions.Clear();
        actionTable.Clear();
        gotoTable.Clear();
        startSymbol = null;
    }

    private bool IsNonTerminal(string symbol)
    {
        // Simple heuristic: non-terminals typically start with uppercase
        return !string.IsNullOrEmpty(symbol) && char.IsUpper(symbol[0]);
    }

    #endregion

    #region Print Methods

    private void PrintProductions()
    {
        Console.WriteLine("\n=== Grammar Productions ===");
        for (int i = 0; i < grammar.Count; i++)
        {
            Console.WriteLine($"{i}: {grammar[i]}");
        }
    }

    private void PrintFirstSets()
    {
        Console.WriteLine("\n=== FIRST Sets ===");
        foreach (var (nonTerminal, firstSet) in first.OrderBy(kv => kv.Key))
        {
            Console.WriteLine($"FIRST({nonTerminal}) = {{ {string.Join(", ", firstSet.OrderBy(s => s))} }}");
        }
    }

    private void PrintFollowSets()
    {
        Console.WriteLine("\n=== FOLLOW Sets ===");
        foreach (var (nonTerminal, followSet) in follow.OrderBy(kv => kv.Key))
        {
            Console.WriteLine($"FOLLOW({nonTerminal}) = {{ {string.Join(", ", followSet.OrderBy(s => s))} }}");
        }
    }

    private void PrintStates()
    {
        Console.WriteLine("\n=== LR(0) States ===");
        for (int i = 0; i < states.Count; i++)
        {
            Console.WriteLine($"State {i}:");
            foreach (var item in states[i].OrderBy(item => item.ToString()))
            {
                Console.WriteLine($"  {item}");
            }
            Console.WriteLine();
        }
    }

    private void PrintParsingTables()
    {
        Console.WriteLine("\n=== ACTION Table ===");
        foreach (var ((state, symbol), action) in actionTable.OrderBy(kv => kv.Key.state).ThenBy(kv => kv.Key.symbol))
        {
            Console.WriteLine($"ACTION[{state}, {symbol}] = {action}");
        }

        Console.WriteLine("\n=== GOTO Table ===");
        foreach (var ((state, symbol), target) in gotoTable.OrderBy(kv => kv.Key.state).ThenBy(kv => kv.Key.symbol))
        {
            Console.WriteLine($"GOTO[{state}, {symbol}] = {target}");
        }
    }

    #endregion

    #region Test Methods

    public void RunTests()
    {
        Console.WriteLine("=== Running Parser Tests ===\n");

        // Test 1: Simple arithmetic grammar
        TestArithmeticGrammar();
        
        // Test 2: Error handling
        TestErrorHandling();
        
        // Test 3: Complex nested structures with multiple epsilon productions
        TestComplexNestedEpsilon();
        
        // Test 4: Multiple recursive patterns with conflict detection
        TestMultipleRecursivePatterns();
        
        // Test 5: Deep nesting with mixed operators and precedence
        TestDeepNestingWithMixedOperators();
        
        Console.WriteLine("=== All Tests Completed ===\n");
    }

    private void TestArithmeticGrammar()
    {
        Console.WriteLine("Test 1: Simple Arithmetic Grammar");
        
        try
        {
            var grammarRules = new List<(string, List<List<string>>)>
            {
                ("E", new List<List<string>> 
                { 
                    new List<string> { "E", "+", "T" },
                    new List<string> { "T" }
                }),
                ("T", new List<List<string>> 
                { 
                    new List<string> { "T", "*", "F" },
                    new List<string> { "F" }
                }),
                ("F", new List<List<string>> 
                { 
                    new List<string> { "(", "E", ")" },
                    new List<string> { "id" }
                })
            };

            LoadGrammar(grammarRules);
            BuildParser();

            // Test valid input
            var validInput = new List<string> { "id", "+", "id", "*", "id" };
            bool result = ParseInput(validInput);
            Console.WriteLine($"Parsing 'id + id * id': {(result ? "SUCCESS" : "FAILED")}");

            // Test invalid input
            var invalidInput = new List<string> { "id", "+", "*", "id" };
            result = ParseInput(invalidInput);
            Console.WriteLine($"Parsing 'id + * id': {(result ? "UNEXPECTED SUCCESS" : "CORRECTLY FAILED")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Test 1 failed with exception: {ex.Message}");
        }
        
        Console.WriteLine();
    }

    private void TestErrorHandling()
    {
        Console.WriteLine("Test 2: Error Handling");
        
        try
        {
            // Test invalid grammar
            var invalidGrammar = new List<(string, List<List<string>>)>
            {
                ("", new List<List<string>> { new List<string> { "a" } }) // Empty LHS
            };

            LoadGrammar(invalidGrammar);
            Console.WriteLine("ERROR: Should have thrown exception for empty LHS");
        }
        catch (ArgumentException)
        {
            Console.WriteLine("SUCCESS: Correctly caught invalid grammar exception");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: Unexpected exception: {ex.Message}");
        }
        
        Console.WriteLine();
    }

private void TestComplexNestedEpsilon()
    {
        Console.WriteLine("Test 3: Complex Nested Structures with Multiple Epsilon Productions");
        
        try
        {
            // Grammar: S → A B C, A → a A | ε, B → b B | ε, C → c C | ε
            // Tests multiple recursive epsilon productions in sequence
            var grammarRules = new List<(string, List<List<string>>)>
            {
                ("S", new List<List<string>> 
                { 
                    new List<string> { "A", "B", "C" }
                }),
                ("A", new List<List<string>> 
                { 
                    new List<string> { "a", "A" },
                    new List<string> { "epsilon" }
                }),
                ("B", new List<List<string>> 
                { 
                    new List<string> { "b", "B" },
                    new List<string> { "epsilon" }
                }),
                ("C", new List<List<string>> 
                { 
                    new List<string> { "c", "C" },
                    new List<string> { "epsilon" }
                })
            };

            LoadGrammar(grammarRules);
            BuildParser();

            // Test Case 1: All epsilon productions
            var input1 = new List<string> { "$" };
            bool result1 = ParseInput(input1);
            Console.WriteLine($"Parsing empty string (all epsilon): {(result1 ? "SUCCESS" : "FAILED")}");

            // Test Case 2: Mixed epsilon and non-epsilon
            var input2 = new List<string> { "a", "b", "$" };
            bool result2 = ParseInput(input2);
            Console.WriteLine($"Parsing 'a b' (C → ε): {(result2 ? "SUCCESS" : "FAILED")}");

            // Test Case 3: Multiple recursions
            var input3 = new List<string> { "a", "a", "b", "b", "c", "c", "$" };
            bool result3 = ParseInput(input3);
            Console.WriteLine($"Parsing 'a a b b c c': {(result3 ? "SUCCESS" : "FAILED")}");

            // Test Case 4: Invalid sequence
            var input4 = new List<string> { "a", "c", "b", "$" };
            bool result4 = ParseInput(input4);
            Console.WriteLine($"Parsing 'a c b' (wrong order): {(result4 ? "UNEXPECTED SUCCESS" : "CORRECTLY FAILED")}");

        }
        catch (Exception ex)
        {
            Console.WriteLine($"Test 3 failed with exception: {ex.Message}");
        }
        
        Console.WriteLine();
    }

    private void TestMultipleRecursivePatterns()
    {
        Console.WriteLine("Test 4: Multiple Recursive Patterns with Conflict Detection");
        
        try
        {
            // Grammar: S → comma L | semi R, L → L , id | id, R → R ; id | id
            // Fixed: Use different starting symbols to avoid conflict
            var grammarRules = new List<(string, List<List<string>>)>
            {
                ("S", new List<List<string>> 
                { 
                    new List<string> { "comma", "L" },
                    new List<string> { "semi", "R" }
                }),
                ("L", new List<List<string>> 
                { 
                    new List<string> { "L", ",", "id" },
                    new List<string> { "id" }
                }),
                ("R", new List<List<string>> 
                { 
                    new List<string> { "R", ";", "id" },
                    new List<string> { "id" }
                })
            };

            LoadGrammar(grammarRules);
            BuildParser();

            // Test Case 1: Comma-separated list
            var input1 = new List<string> { "comma", "id", ",", "id", ",", "id", "$" };
            bool result1 = ParseInput(input1);
            Console.WriteLine($"Parsing comma list 'comma id,id,id': {(result1 ? "SUCCESS" : "FAILED")}");

            // Test Case 2: Semicolon-separated list  
            var input2 = new List<string> { "semi", "id", ";", "id", ";", "id", "$" };
            bool result2 = ParseInput(input2);
            Console.WriteLine($"Parsing semicolon list 'semi id;id;id': {(result2 ? "SUCCESS" : "FAILED")}");

            // Test Case 3: Single identifier with comma
            var input3 = new List<string> { "comma", "id", "$" };
            bool result3 = ParseInput(input3);
            Console.WriteLine($"Parsing single 'comma id': {(result3 ? "SUCCESS" : "FAILED")}");

            // Test Case 4: Single identifier with semi
            var input4 = new List<string> { "semi", "id", "$" };
            bool result4 = ParseInput(input4);
            Console.WriteLine($"Parsing single 'semi id': {(result4 ? "SUCCESS" : "FAILED")}");

            // Test Case 5: Invalid mixed structure (should fail)
            var input5 = new List<string> { "comma", "id", ";", "id", "$" };
            bool result5 = ParseInput(input5);
            Console.WriteLine($"Parsing mixed 'comma id;id': {(result5 ? "UNEXPECTED SUCCESS" : "CORRECTLY FAILED")}");

        }
        catch (Exception ex)
        {
            Console.WriteLine($"Test 4 failed with exception: {ex.Message}");
        }
        
        Console.WriteLine();
    }

    private void TestDeepNestingWithMixedOperators()
    {
        Console.WriteLine("Test 5: Deep Nesting with Mixed Operators and Precedence");
        
        try
        {
            // Grammar: S → E, E → E + T | E - T | T, T → T * F | T / F | F, F → - F | + F | ( E ) | id | num
            // Tests complex operator precedence with unary operators and deep nesting
            var grammarRules = new List<(string, List<List<string>>)>
            {
                ("S", new List<List<string>> 
                { 
                    new List<string> { "E" }
                }),
                ("E", new List<List<string>> 
                { 
                    new List<string> { "E", "+", "T" },
                    new List<string> { "E", "-", "T" },
                    new List<string> { "T" }
                }),
                ("T", new List<List<string>> 
                { 
                    new List<string> { "T", "*", "F" },
                    new List<string> { "T", "/", "F" },
                    new List<string> { "F" }
                }),
                ("F", new List<List<string>> 
                { 
                    new List<string> { "-", "F" },
                    new List<string> { "+", "F" },
                    new List<string> { "(", "E", ")" },
                    new List<string> { "id" },
                    new List<string> { "num" }
                })
            };

            LoadGrammar(grammarRules);
            BuildParser();

            // Test Case 1: Complex nested expression with unary operators
            var input1 = new List<string> { "(", "-", "id", "+", "num", ")", "*", "-", "(", "id", "/", "num", ")", "$" };
            bool result1 = ParseInput(input1);
            Console.WriteLine($"Parsing '(-id + num) * -(id / num)': {(result1 ? "SUCCESS" : "FAILED")}");

            // Test Case 2: Multiple levels of nesting
            var input2 = new List<string> { "(", "(", "(", "id", ")", ")", ")", "$" };
            bool result2 = ParseInput(input2);
            Console.WriteLine($"Parsing '(((id)))': {(result2 ? "SUCCESS" : "FAILED")}");

            // Test Case 3: Unary operator chains
            var input3 = new List<string> { "-", "+", "-", "id", "$" };
            bool result3 = ParseInput(input3);
            Console.WriteLine($"Parsing '-+-id': {(result3 ? "SUCCESS" : "FAILED")}");

            // Test Case 4: Complex operator precedence
            var input4 = new List<string> { "id", "+", "num", "*", "id", "-", "num", "/", "id", "$" };
            bool result4 = ParseInput(input4);
            Console.WriteLine($"Parsing 'id + num * id - num / id': {(result4 ? "SUCCESS" : "FAILED")}");

            // Test Case 5: Unbalanced parentheses (should fail)
            var input5 = new List<string> { "(", "id", "+", "num", "$" };
            bool result5 = ParseInput(input5);
            Console.WriteLine($"Parsing '(id + num' (unbalanced): {(result5 ? "UNEXPECTED SUCCESS" : "CORRECTLY FAILED")}");

            // Test Case 6: Invalid operator sequence (should fail)
            var input6 = new List<string> { "id", "+", "*", "num", "$" };
            bool result6 = ParseInput(input6);
            Console.WriteLine($"Parsing 'id + * num' (invalid): {(result6 ? "UNEXPECTED SUCCESS" : "CORRECTLY FAILED")}");

        }
        catch (Exception ex)
        {
            Console.WriteLine($"Test 5 failed with exception: {ex.Message}");
        }
        
        Console.WriteLine();
    }

    #endregion

    #region Main Method

    public static void Main()
    {
        var parser = new EnhancedLR0Parser();
        
        try
        {
            parser.RunTests();
            
            Console.WriteLine("Choose an option:");
            Console.WriteLine("1. Enter grammar interactively");
            Console.WriteLine("2. Use default arithmetic grammar");
            Console.WriteLine("3. Exit");
            
            var choice = Console.ReadLine();
            
            switch (choice)
            {
                case "1":
                    parser.InputGrammarInteractively();
                    parser.BuildParser();
                    parser.PrintGrammarInfo();
                    parser.InteractiveParsingSession();
                    break;
                    
                case "2":
                    parser.LoadDefaultArithmeticGrammar();
                    parser.BuildParser();
                    parser.PrintGrammarInfo();
                    parser.InteractiveParsingSession();
                    break;
                    
                case "3":
                default:
                    Console.WriteLine("Goodbye!");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
        }
    }

    private void LoadDefaultArithmeticGrammar()
    {
        var grammarRules = new List<(string, List<List<string>>)>
        {
            ("E", new List<List<string>> 
            { 
                new List<string> { "E", "+", "T" },
                new List<string> { "T" }
            }),
            ("T", new List<List<string>> 
            { 
                new List<string> { "T", "*", "F" },
                new List<string> { "F" }
            }),
            ("F", new List<List<string>> 
            { 
                new List<string> { "(", "E", ")" },
                new List<string> { "id" }
            })
        };

        LoadGrammar(grammarRules);
        Console.WriteLine("Loaded default arithmetic grammar:");
        Console.WriteLine("E -> E + T | T");
        Console.WriteLine("T -> T * F | F");
        Console.WriteLine("F -> ( E ) | id");
    }

    private void InteractiveParsingSession()
    {
        Console.WriteLine("\n=== Interactive Parsing Session ===");
        Console.WriteLine("Enter tokens separated by spaces (e.g., 'id + id * id')");
        Console.WriteLine("Type 'quit' to exit");

        string input;
        while ((input = Console.ReadLine()?.Trim()) != "quit" && !string.IsNullOrEmpty(input))
        {
            try
            {
                var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                bool result = ParseInput(tokens);
                Console.WriteLine($"Result: {(result ? "ACCEPTED" : "REJECTED")}\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}\n");
            }
        }
    }

    #endregion
}