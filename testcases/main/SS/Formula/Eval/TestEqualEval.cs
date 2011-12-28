/* ====================================================================
   Licensed to the Apache Software Foundation (ASF) under one or more
   contributor license agreements.  See the NOTICE file distributed with
   this work for Additional information regarding copyright ownership.
   The ASF licenses this file to You under the Apache License, Version 2.0
   (the "License"); you may not use this file except in compliance with
   the License.  You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
==================================================================== */

namespace NPOI.SS.Formula.Eval;

using junit.framework.AssertionFailedError;
using junit.framework.TestCase;

using NPOI.SS.Formula.functions.EvalFactory;
using NPOI.SS.Formula.functions.Function;

/**
 * Test for {@link EqualEval}
 *
 * @author Josh Micich
 */
public class TestEqualEval  {
	// convenient access to namepace
	private static EvalInstances EI = null;

	/**
	 * Test for bug observable at svn revision 692218 (Sep 2008)<br/>
	 * The value from a 1x1 area should be taken immediately, regardless of srcRow and srcCol
	 */
	public void Test1x1AreaOperand() {

		ValueEval[] values = { BoolEval.FALSE, };
		ValueEval[] args = {
			EvalFactory.CreateAreaEval("B1:B1", values),
			BoolEval.FALSE,
		};
		ValueEval result = Evaluate(EI.Equal, args, 10, 10);
		if (result is ErrorEval) {
			if (result == ErrorEval.VALUE_INVALID) {
				throw new AssertionFailedError("Identified bug in Evaluation of 1x1 area");
			}
		}
		Assert.AreEqual(BoolEval.class, result.GetType());
		Assert.IsTrue(((BoolEval)result).GetBooleanValue());
	}
	/**
	 * Empty string is equal to blank
	 */
	public void TestBlankEqualToEmptyString() {

		ValueEval[] args = {
			new StringEval(""),
			BlankEval.instance,
		};
		ValueEval result = Evaluate(EI.Equal, args, 10, 10);
		Assert.AreEqual(BoolEval.class, result.GetType());
		BoolEval be = (BoolEval) result;
		if (!be.GetBooleanValue()) {
			throw new AssertionFailedError("Identified bug blank/empty string Equality");
		}
		Assert.IsTrue(be.GetBooleanValue());
	}

	/**
	 * Test for bug 46613 (observable at svn r737248)
	 */
	public void TestStringInsensitive_bug46613() {
		if (!EvalStringCmp("abc", "aBc", EI.Equal)) {
			throw new AssertionFailedError("Identified bug 46613");
		}
		Assert.IsTrue(EvalStringCmp("abc", "aBc", EI.Equal));
		Assert.IsTrue(EvalStringCmp("ABC", "azz", EI.LessThan));
		Assert.IsTrue(EvalStringCmp("abc", "AZZ", EI.LessThan));
		Assert.IsTrue(EvalStringCmp("ABC", "aaa", EI.GreaterThan));
		Assert.IsTrue(EvalStringCmp("abc", "AAA", EI.GreaterThan));
	}

	private static bool EvalStringCmp(String a, String b, Function cmpOp) {
		ValueEval[] args = {
			new StringEval(a),
			new StringEval(b),
		};
		ValueEval result = Evaluate(cmpOp, args, 10, 20);
		Assert.AreEqual(BoolEval.class, result.GetType());
		BoolEval be = (BoolEval) result;
		return be.GetBooleanValue();
	}

	public void TestBooleanCompares() {
		ConfirmCompares(BoolEval.TRUE, new StringEval("TRUE"), +1);
		ConfirmCompares(BoolEval.TRUE, new NumberEval(1.0), +1);
		ConfirmCompares(BoolEval.TRUE, BoolEval.TRUE, 0);
		ConfirmCompares(BoolEval.TRUE, BoolEval.FALSE, +1);

		ConfirmCompares(BoolEval.FALSE, new StringEval("TRUE"), +1);
		ConfirmCompares(BoolEval.FALSE, new StringEval("FALSE"), +1);
		ConfirmCompares(BoolEval.FALSE, new NumberEval(0.0), +1);
		ConfirmCompares(BoolEval.FALSE, BoolEval.FALSE, 0);
	}
	private static void ConfirmCompares(ValueEval a, ValueEval b, int expRes) {
		Confirm(a, b, expRes>0,  EI.GreaterThan);
		Confirm(a, b, expRes>=0, EI.GreaterEqual);
		Confirm(a, b, expRes==0, EI.Equal);
		Confirm(a, b, expRes<=0, EI.LessEqual);
		Confirm(a, b, expRes<0,  EI.LessThan);

		Confirm(b, a, expRes<0,  EI.GreaterThan);
		Confirm(b, a, expRes<=0, EI.GreaterEqual);
		Confirm(b, a, expRes==0, EI.Equal);
		Confirm(b, a, expRes>=0, EI.LessEqual);
		Confirm(b, a, expRes>0,  EI.LessThan);
	}
	private static void Confirm(ValueEval a, ValueEval b, bool expectedResult, Function cmpOp) {
		ValueEval[] args = { a, b, };
		ValueEval result = Evaluate(cmpOp, args, 10, 20);
		Assert.AreEqual(BoolEval.class, result.GetType());
		Assert.AreEqual(expectedResult, ((BoolEval) result).GetBooleanValue());
	}

	/**
	 * Bug 47198 involved a formula "-A1=0" where cell A1 was 0.0.
	 * Excel Evaluates "-A1=0" to TRUE, not because it thinks -0.0==0.0
	 * but because "-A1" Evaluated to +0.0
	 * <p/>
	 * Note - the original diagnosis of bug 47198 was that
	 * "Excel considers -0.0 to be equal to 0.0" which is NQR
	 * See {@link TestMinusZeroResult} for more specific Tests regarding -0.0.
	 */
	public void TestZeroEquality_bug47198() {
		NumberEval zero = new NumberEval(0.0);
		NumberEval mZero = (NumberEval) Evaluate(UnaryMinusEval.instance, new ValueEval[] { zero, }, 0, 0);
		if (Double.doubleToLongBits(mZero.GetNumberValue()) == 0x8000000000000000L) {
			throw new AssertionFailedError("Identified bug 47198: unary minus should convert -0.0 to 0.0");
		}
		ValueEval[] args = { zero, mZero, };
		BoolEval result = (BoolEval) Evaluate(EI.Equal, args, 0, 0);
		if (!result.GetBooleanValue()) {
			throw new AssertionFailedError("Identified bug 47198: -0.0 != 0.0");
		}
	}

	public void TestRounding_bug47598() {
		double x = 1+1.0028-0.9973; // should be 1.0055, but has IEEE rounding
		Assert.IsFalse(x == 1.0055);

		NumberEval a = new NumberEval(x);
		NumberEval b = new NumberEval(1.0055);
		Assert.AreEqual("1.0055", b.StringValue);

		ValueEval[] args = { a, b, };
		BoolEval result = (BoolEval) Evaluate(EI.Equal, args, 0, 0);
		if (!result.GetBooleanValue()) {
			throw new AssertionFailedError("Identified bug 47598: 1+1.0028-0.9973 != 1.0055");
		}
	}

	private static ValueEval Evaluate(Function oper, ValueEval[] args, int srcRowIx, int srcColIx) {
		return oper.Evaluate(args, srcRowIx, (short) srcColIx);
	}
}

