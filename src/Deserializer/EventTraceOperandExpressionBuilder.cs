using System.Linq.Expressions;
using System.Reflection;

namespace Nefarius.Utilities.ETW.Deserializer;

internal static class EventTraceOperandExpressionBuilder
{
    public static Expression Build(
        IEventTraceOperand operand,
        ParameterExpression eventRecordReader,
        ParameterExpression eventRecordWriter,
        ParameterExpression eventMetadataTable,
        ParameterExpression runtimeMetadata)
    {
        return new EventTraceOperandExpressionBuilderImpl().Build(operand, eventRecordReader, eventRecordWriter,
            eventMetadataTable, runtimeMetadata);
    }

    public static MethodCallExpression Call(this ParameterExpression instance, string methodName,
        params Expression[] arguments)
    {
        Type[] parameterTypes = arguments.Select(t => t.Type).ToArray();
        MethodInfo? methodInfo = instance.Type.GetMethod(methodName, parameterTypes);
        return Expression.Call(instance, methodInfo, arguments);
    }
}

internal sealed class EventTraceOperandExpressionBuilderImpl
{
    public Expression Build(IEventTraceOperand operand, ParameterExpression eventRecordReader,
        ParameterExpression eventRecordWriter, ParameterExpression eventMetadataTable,
        ParameterExpression runtimeMetadata)
    {
        ParameterExpression eventMetadata = Expression.Parameter(typeof(EventMetadata));
        ParameterExpression properties = Expression.Parameter(typeof(PropertyMetadata[]));

        List<ParameterExpression> variables = new() { eventMetadata, properties };

        ExpressionGenerator expGenerator = new(eventRecordReader, eventRecordWriter, properties);
        List<Expression> list = new()
        {
            Expression.Assign(eventMetadata,
                Expression.ArrayAccess(eventMetadataTable, Expression.Constant(operand.EventMetadataTableIndex))),
            Expression.Assign(properties, Expression.PropertyOrField(eventMetadata, "Properties")),
            eventRecordWriter.Call("WriteEventBegin", eventMetadata, runtimeMetadata),
            expGenerator.CodeGenerate(operand.EventPropertyOperands),
            eventRecordWriter.Call("WriteEventEnd")
        };

        BlockExpression returnExpression = Expression.Block(variables, list);
        return returnExpression;
    }

    private sealed class ExpressionGenerator
    {
        private readonly ParameterExpression eventRecordReader;

        private readonly ParameterExpression eventRecordWriter;
        private readonly Dictionary<IEventTracePropertyOperand, Expression> operandReferenceTable = new();

        private readonly ParameterExpression properties;

        public ExpressionGenerator(ParameterExpression eventRecordReader, ParameterExpression eventRecordWriter,
            ParameterExpression properties)
        {
            this.eventRecordReader = eventRecordReader;
            this.eventRecordWriter = eventRecordWriter;
            this.properties = properties;
        }

        public Expression CodeGenerate(IEnumerable<IEventTracePropertyOperand> operands)
        {
            List<ParameterExpression> variables = new();
            List<Expression> list = new();

            foreach (IEventTracePropertyOperand operand in operands)
            {
                _TDH_IN_TYPE inType = operand.Metadata.InType;
                Expression c; /* running expression for this operand */

                list.Add(eventRecordWriter.Call("WritePropertyBegin",
                    Expression.ArrayAccess(properties, Expression.Constant(operand.PropertyIndex))));

                /* if struct, recurse */
                if (operand.Children.Count > 0)
                {
                    c = Expression.Block(
                        eventRecordWriter.Call("WriteStructBegin"),
                        CodeGenerate(operand.Children),
                        eventRecordWriter.Call("WriteStructEnd"));
                }
                else
                {
                    Expression readValue;

                    /* otherwise, if operand has a length parameter, look it up or make constant */
                    if (operand.IsVariableLength || operand.IsFixedLength)
                    {
                        Expression length = operand.IsVariableLength
                            ? operandReferenceTable[operand.VariableLengthSize]
                            : Expression.Constant(operand.FixedLengthSize);

                        readValue = Call(eventRecordReader, inType.ReadMethodInfo(eventRecordReader.Type, length.Type),
                            length);
                    }
                    /* otherwise, it's just a normal call, no args */
                    else
                    {
                        readValue = Call(eventRecordReader, inType.ReadMethodInfo(eventRecordReader.Type));
                    }

                    /* save the operand because someone else maybe needing it */
                    /* and change the running variable */
                    if (operand.IsReferencedByOtherProperties)
                    {
                        ParameterExpression local = Expression.Parameter(inType.CSharpType(), operand.Metadata.Name);
                        operandReferenceTable.Add(operand, local);
                        variables.Add(local);
                        c = Expression.Block(Expression.Assign(local, readValue),
                            Call(eventRecordWriter, inType.WriteMethodInfo(eventRecordWriter.Type, local.Type), local));
                    }
                    else
                    {
                        c = Call(eventRecordWriter, inType.WriteMethodInfo(eventRecordWriter.Type, inType.CSharpType()),
                            readValue);
                    }
                }

                if (operand.IsVariableArray || operand.IsFixedArray)
                {
                    ParameterExpression loopVariable = Expression.Parameter(typeof(int));
                    variables.Add(loopVariable);

                    Expression end = operand.IsVariableArray
                        ? operandReferenceTable[operand.VariableArraySize]
                        : Expression.Constant(operand.FixedArraySize);

                    Expression expr = loopVariable;
                    ConvertIfNecessary(ref expr, ref end);
                    list.Add(eventRecordWriter.Call("WriteArrayBegin"));
                    list.Add(For(loopVariable, Expression.Constant(0), Expression.LessThan(expr, end),
                        Expression.AddAssign(loopVariable, Expression.Constant(1)), c));
                    list.Add(eventRecordWriter.Call("WriteArrayEnd"));
                }
                else
                {
                    list.Add(c);
                }

                list.Add(eventRecordWriter.Call("WritePropertyEnd"));
            }

            return list.Count == 0 ? Expression.Empty() : Expression.Block(variables, list);
        }

        private static MethodCallExpression Call(ParameterExpression instance, MethodInfo methodInfo,
            params Expression[] arguments)
        {
            return Expression.Call(instance, methodInfo, arguments);
        }

        private static Expression For(ParameterExpression parameter, Expression initial, Expression condition,
            Expression increment, params Expression[] body)
        {
            LabelTarget breakLabel = Expression.Label("break");
            BlockExpression loop = Expression.Block(
                new[] { parameter },
                Expression.Assign(parameter, initial),
                Expression.Loop(
                    Expression.IfThenElse(
                        condition,
                        Expression.Block(
                            body.Concat(new[] { increment })),
                        Expression.Break(breakLabel)),
                    breakLabel));

            return loop;
        }

        private static void ConvertIfNecessary(ref Expression left, ref Expression right)
        {
            TypeCode leftTypeCode = Type.GetTypeCode(left.Type);
            TypeCode rightTypeCode = Type.GetTypeCode(right.Type);

            if (leftTypeCode == rightTypeCode)
            {
                return;
            }

            if (leftTypeCode > rightTypeCode)
            {
                right = Expression.Convert(right, left.Type);
            }
            else
            {
                left = Expression.Convert(left, right.Type);
            }
        }
    }
}