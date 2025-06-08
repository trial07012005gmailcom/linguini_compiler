# EnhancedLR0Parser Documentation

## Overview

The `EnhancedLR0Parser` is a C# implementation of an LR(0) parser, designed to parse strings according to a context-free grammar (CFG). It supports the construction of an LR(0) automaton, parsing table generation, and parsing of input token sequences. The parser handles epsilon productions, validates grammars, and provides detailed debugging output for grammar analysis and parsing steps. It is suitable for educational purposes or as a foundation for more advanced parsing algorithms.

## Purpose

The parser is designed to:
- Load and validate context-free grammars.
- Compute FIRST and FOLLOW sets for parsing table construction.
- Build an LR(0) automaton and generate ACTION and GOTO tables.
- Parse input token sequences, reporting whether they are accepted or rejected.
- Provide interactive grammar input and parsing capabilities.
- Handle error conditions and detect shift/reduce or reduce/reduce conflicts.

## Key Components

### Data Structures
The parser uses several custom data structures to manage grammar and parsing information:

- **`Production` Class**
  - Represents a production rule in the grammar (e.g., A → α).
  - Properties: `LHS` (left-hand side, a non-terminal), `RHS` (right-hand side, a list of symbols).
  - Methods: Overrides `ToString`, `Equals`, and `GetHashCode` for string representation and comparison.

- **`Item` Class**
  - Represents an LR(0) item, which is a production with a dot indicating the parsing position (e.g., A → α • β).
  - Properties: `LHS`, `RHS`, `Dot` (position of the dot), `IsComplete` (whether the dot is at the end), `CanAdvance` (whether the dot can move forward), `NextSymbol` (the symbol after the dot).
  - Methods: Overrides `ToString`, `Equals`, and `GetHashCode` for string representation and comparison.

- **`ParserException` Class**
  - A custom exception class for parser-specific errors, such as grammar validation failures or parsing conflicts.

### Fields
- `grammar`: List of `Production` objects representing the grammar.
- `terminals`: Set of terminal symbols.
- `nonTerminals`: Set of non-terminal symbols.
- `first`: Dictionary mapping non-terminals to their FIRST sets.
- `follow`: Dictionary mapping non-terminals to their FOLLOW sets.
- `startSymbol`: The start symbol of the grammar.
- `states`: List of states in the LR(0) automaton, each a set of `Item` objects.
- `transitions`: Dictionary mapping state-symbol pairs to target states.
- `actionTable`: Dictionary for shift/reduce/accept actions.
- `gotoTable`: Dictionary for state transitions on non-terminals.

### Constants
- `EPSILON`: Represents an epsilon (empty) production ("epsilon").
- `END_MARKER`: End-of-input marker ("$").
- `AUGMENTED_SUFFIX`: Suffix for the augmented start symbol ("'").

## Key Methods

### Grammar Loading and Validation
- **`LoadGrammar(List<(string lhs, List<List<string>> rhsList)> grammarRules)`**
  - Loads a grammar from a list of tuples, where each tuple contains a non-terminal (LHS) and a list of RHS productions.
  - Validates the grammar, ensuring non-empty rules and valid LHS.
  - Identifies terminals and non-terminals, and sets the start symbol.
- **`InputGrammarInteractively()`**
  - Allows interactive grammar input via the console in the format `A -> a B | b`.
  - Parses input lines and calls `LoadGrammar`.
- **`ValidateGrammar()`**
  - Checks for unreachable and undefined non-terminals, issuing warnings or throwing exceptions as needed.

### FIRST and FOLLOW Set Computation
- **`ComputeFirstSets()`**
  - Computes the FIRST sets for all non-terminals, handling epsilon productions.
- **`ComputeFollowSets()`**
  - Computes the FOLLOW sets for all non-terminals, using FIRST sets and grammar rules.
- **`ComputeFirstOfSequence(List<string> symbols)`**
  - Helper method to compute the FIRST set of a symbol sequence.

### LR(0) Automaton Construction
- **`BuildLR0Automaton()`**
  - Constructs the LR(0) automaton by creating states and transitions.
  - Augments the grammar with a start production (S' → S).
- **`Closure(HashSet<Item> items)`**
  - Computes the closure of a set of LR(0) items by adding all relevant productions.
- **`Goto(HashSet<Item> items, string symbol)`**
  - Computes the GOTO set for a state and symbol, advancing the dot in items.
- **`FindOrCreateState(HashSet<Item> newState)`**
  - Finds an existing state or creates a new one in the automaton.

### Parsing Table Construction
- **`BuildParsingTables()`**
  - Generates ACTION and GOTO tables based on the LR(0) automaton.
  - Handles shift, reduce, and accept actions, detecting conflicts (shift/reduce or reduce/reduce).
- **`FindProductionIndex(string lhs, List<string> rhs)`**
  - Finds the index of a production in the grammar for reduction actions.

### Parsing Simulation
- **`ParseInput(List<string> inputTokens)`**
  - Parses a list of input tokens, appending the end marker if needed.
  - Returns `true` if the input is accepted, `false` otherwise.
- **`SimulateParser(List<string> input)`**
  - Implements the LR(0) parsing algorithm using state and symbol stacks.
  - Performs shift, reduce, or accept actions based on the ACTION table.
  - Outputs parsing steps and errors to the console.

### Utility and Debugging
- **`PrintGrammarInfo()`**
  - Prints grammar productions, FIRST sets, FOLLOW sets, LR(0) states, and parsing tables.
- **`Clear()`**
  - Resets all parser fields to their initial state.
- **`IsNonTerminal(string symbol)`**
  - Heuristic to identify non-terminals (symbols starting with an uppercase letter).

### Test and Main Methods
- **`RunTests()`**
  - Runs predefined tests, including a simple arithmetic grammar and error handling cases.
- **`TestArithmeticGrammar()`**
  - Tests parsing of a simple arithmetic grammar (E → E + T | T, T → T * F | F, F → ( E ) | id).
- **`TestErrorHandling()`**
  - Tests error handling for invalid grammar input.
- **`Main()`**
  - Entry point offering options to run tests, input grammar interactively, use a default arithmetic grammar, or exit.
- **`LoadDefaultArithmeticGrammar()`**
  - Loads a default arithmetic grammar for testing.
- **`InteractiveParsingSession()`**
  - Allows interactive parsing of token sequences via the console.

## Usage

### Loading a Grammar
To use the parser, first load a grammar using `LoadGrammar` or `InputGrammarInteractively`. The grammar is specified as a list of tuples, where each tuple contains a non-terminal and a list of possible RHS productions. For example:

```csharp
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

var parser = new EnhancedLR0Parser();
parser.LoadGrammar(grammarRules);
```

### Building the Parser
After loading the grammar, call `BuildParser` to compute FIRST/FOLLOW sets, construct the LR(0) automaton, and generate parsing tables:

```csharp
parser.BuildParser();
```

### Parsing Input
Parse a sequence of tokens using `ParseInput`. The method returns `true` if the input is accepted, `false` otherwise:

```csharp
var input = new List<string> { "id", "+", "id", "*", "id", "$" };
bool result = parser.ParseInput(input); // Returns true for valid input
```

### Debugging
Use `PrintGrammarInfo` to inspect the grammar, FIRST/FOLLOW sets, states, and parsing tables:

```csharp
parser.PrintGrammarInfo();
```

### Interactive Mode
Run the `Main` method to access an interactive console interface, allowing grammar input and parsing sessions:

```csharp
EnhancedLR0Parser.Main();
```

## Limitations
- **LR(0) Restrictions**: The parser uses the LR(0) algorithm, which does not use lookahead, making it unsuitable for ambiguous grammars or those requiring lookahead to resolve conflicts.
- **Conflict Detection**: Detects shift/reduce and reduce/reduce conflicts but does not resolve them, throwing a `ParserException` instead.
- **Non-Terminal Heuristic**: Assumes non-terminals start with uppercase letters, which may not suit all grammars.
- **Performance**: May be inefficient for large grammars due to the exhaustive closure and GOTO computations.

## Example Grammar
The default arithmetic grammar included in the code is:

```
E -> E + T | T
T -> T * F | F
F -> ( E ) | id
```

This grammar supports expressions like `id + id * id` or `(id + id) * id`. The parser can be extended to support other grammars by modifying the input to `LoadGrammar`.

## Error Handling
The parser includes robust error handling:
- Throws `ArgumentException` for invalid grammar inputs (e.g., empty LHS, null RHS).
- Throws `ParserException` for parsing conflicts or missing productions.
- Outputs detailed error messages during parsing simulation, including valid actions for the current state.

## Extensibility
The code can be extended to:
- Support LR(1) or SLR(1) parsing by modifying the parsing table construction to include lookahead.
- Add more sophisticated grammar validation or conflict resolution strategies.
- Enhance the interactive interface with additional features, such as saving grammars or visualizing parse trees.

## Dependencies
- Requires .NET Framework or .NET Core for C# execution.
- Uses standard libraries: `System`, `System.Collections.Generic`, `System.Linq`, `System.Text`.

,省

## Conclusion
The `EnhancedLR0Parser` is a robust implementation of an LR(0) parser, ideal for educational purposes or as a starting point for more complex parsing systems. It provides comprehensive functionality for grammar processing, automaton construction, and parsing, with clear debugging output and error handling.