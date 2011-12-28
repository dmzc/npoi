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

namespace NPOI.SS.Formula;









using junit.framework.AssertionFailedError;
using junit.framework.TestCase;

using NPOI.hssf.Model.HSSFFormulaParser;
using NPOI.SS.Formula.PTG.Ptg;
using NPOI.SS.Formula.Eval.BlankEval;
using NPOI.SS.Formula.Eval.BoolEval;
using NPOI.SS.Formula.Eval.ErrorEval;
using NPOI.SS.Formula.Eval.NumberEval;
using NPOI.SS.Formula.Eval.StringEval;
using NPOI.SS.Formula.Eval.ValueEval;
using NPOI.hssf.UserModel.FormulaExtractor;
using NPOI.hssf.UserModel.HSSFCell;
using NPOI.hssf.UserModel.HSSFEvaluationTestHelper;
using NPOI.hssf.UserModel.HSSFFormulaEvaluator;
using NPOI.hssf.UserModel.HSSFRow;
using NPOI.hssf.UserModel.HSSFSheet;
using NPOI.hssf.UserModel.HSSFWorkbook;
using NPOI.hssf.Util.CellReference;
using NPOI.SS.Formula.IEvaluationListener.ICacheEntry;
using NPOI.SS.Formula.PlainCellCache.Loc;
using NPOI.SS.UserModel.*;

/**
 * Tests {@link NPOI.SS.Formula.EvaluationCache}.  Makes sure that where possible (previously calculated) cached
 * values are used.  Also Checks that changing cell values causes the correct (minimal) Set of
 * dependent cached values to be Cleared.
 *
 * @author Josh Micich
 */
public class TestEvaluationCache  {

	private static class FormulaCellCacheEntryComparer : Comparator<ICacheEntry> {

		private Dictionary<ICacheEntry,EvaluationCell> _formulaCellsByCacheEntry;

		public FormulaCellCacheEntryComparer(Dictionary<ICacheEntry,EvaluationCell> formulaCellsByCacheEntry) {
			_formulaCellsByCacheEntry = formulaCellsByCacheEntry;
		}
		private EvaluationCell GetCell(ICacheEntry a) {
			return _formulaCellsByCacheEntry.Get(a);
		}
		public int Compare(ICacheEntry oa, ICacheEntry ob) {
			EvaluationCell a = GetCell(oa);
			EvaluationCell b = GetCell(ob);
			int cmp;
			cmp = a.RowIndex - b.RowIndex;
			if (cmp != 0) {
				return cmp;
			}
			cmp = a.ColumnIndex - b.ColumnIndex;
			if (cmp != 0) {
				return cmp;
			}
			if (a.Sheet == b.Sheet) {
				return 0;
			}
			throw new RuntimeException("Incomplete code - don't know how to order sheets");
		}
	}

	private static class EvalListener : EvaluationListener {

		private List<String> _logList;
		private HSSFWorkbook _book;
		private Dictionary<ICacheEntry,EvaluationCell> _formulaCellsByCacheEntry;
		private Dictionary<ICacheEntry,Loc> _plainCellLocsByCacheEntry;

		public EvalListener(HSSFWorkbook wb) {
			_book = wb;
			_logList = new List<String>();
			_formulaCellsByCacheEntry = new Dictionary<ICacheEntry,EvaluationCell>();
			_plainCellLocsByCacheEntry = new Dictionary<ICacheEntry, Loc>();
		}
		public void onCacheHit(int sheetIndex, int rowIndex, int columnIndex, ValueEval result) {
			log("hit", rowIndex, columnIndex, result);
		}
		public void onReadPlainValue(int sheetIndex, int rowIndex, int columnIndex, ICacheEntry entry) {
			Loc loc = new Loc(0, sheetIndex, rowIndex, columnIndex);
			_plainCellLocsByCacheEntry.Put(entry, loc);
			log("value", rowIndex, columnIndex, entry.GetValue());
		}
		public void onStartEvaluate(EvaluationCell cell, ICacheEntry entry) {
			_formulaCellsByCacheEntry.Put(entry, cell);
			HSSFCell hc = _book.GetSheetAt(0).GetRow(cell.RowIndex).GetCell(cell.ColumnIndex);
			log("start", cell.RowIndex, cell.ColumnIndex, FormulaExtractor.GetPtgs(hc));
		}
		public void onEndEvaluate(ICacheEntry entry, ValueEval result) {
			EvaluationCell cell = _formulaCellsByCacheEntry.Get(entry);
			log("end", cell.RowIndex, cell.ColumnIndex, result);
		}
		public void onClearCachedValue(ICacheEntry entry) {
			int rowIndex;
			int columnIndex;
			EvaluationCell cell = _formulaCellsByCacheEntry.Get(entry);
			if (cell == null) {
				Loc loc = _plainCellLocsByCacheEntry.Get(entry);
				if (loc == null) {
					throw new InvalidOperationException("can't find cell or location");
				}
				rowIndex = loc.RowIndex;
				columnIndex = loc.ColumnIndex;
			} else {
				rowIndex = cell.RowIndex;
				columnIndex = cell.ColumnIndex;
			}
			log("Clear", rowIndex, columnIndex, entry.GetValue());
		}
		public void sortDependentCachedValues(ICacheEntry[] entries) {
			Arrays.sort(entries, new FormulaCellCacheEntryComparer(_formulaCellsByCacheEntry));
		}
		public void onClearDependentCachedValue(ICacheEntry entry, int depth) {
			EvaluationCell cell = _formulaCellsByCacheEntry.Get(entry);
			log("Clear" + depth, cell.RowIndex, cell.ColumnIndex, entry.GetValue());
		}

		public void onChangeFromBlankValue(int sheetIndex, int rowIndex, int columnIndex,
				EvaluationCell cell, ICacheEntry entry) {
			log("ChangeFromBlank", rowIndex, columnIndex, entry.GetValue());
			if (entry.GetValue() == null) { // hack to tell the difference between formula and plain value
				// perhaps the API could be improved: onChangeFromBlankToValue, onChangeFromBlankToFormula
				_formulaCellsByCacheEntry.Put(entry, cell);
			} else {
				Loc loc = new Loc(0, sheetIndex, rowIndex, columnIndex);
				_plainCellLocsByCacheEntry.Put(entry, loc);
			}
		}
		private void log(String tag, int rowIndex, int columnIndex, Object value) {
			StringBuilder sb = new StringBuilder(64);
			sb.Append(tag).append(' ');
			sb.Append(new CellReference(rowIndex, columnIndex, false, false).formatAsString());
			if (value != null) {
				sb.Append(' ').Append(formatValue(value));
			}
			_logList.Add(sb.ToString());
		}
		private String formatValue(Object value) {
			if (value is Ptg[]) {
				Ptg[] ptgs = (Ptg[]) value;
				return HSSFFormulaParser.ToFormulaString(_book, ptgs);
			}
			if (value is NumberEval) {
				NumberEval ne = (NumberEval) value;
				return ne.StringValue;
			}
			if (value is StringEval) {
				StringEval se = (StringEval) value;
				return "'" + se.StringValue + "'";
			}
			if (value is BoolEval) {
				BoolEval be = (BoolEval) value;
				return be.StringValue;
			}
			if (value == BlankEval.instance) {
				return "#BLANK#";
			}
			if (value is ErrorEval) {
				ErrorEval ee = (ErrorEval) value;
				return ErrorEval.GetText(ee.GetErrorCode());
			}
			throw new ArgumentException("Unexpected value class ("
					+ value.GetType().GetName() + ")");
		}
		public String[] GetAndClearLog() {
			String[] result = new String[_logList.Count];
			_logList.ToArray(result);
			_logList.Clear();
			return result;
		}
	}
	/**
	 * Wrapper class to manage repetitive tasks from this Test,
	 *
	 * Note - this class does a little bit more than just plain Set-up of data. The method
	 * {@link WorkbookEvaluator#ClearCachedResultValue(HSSFSheet, int, int)} is called whenever a
	 * cell value is Changed.
	 *
	 */
	private static class MySheet {

		private HSSFSheet _sheet;
		private WorkbookEvaluator _Evaluator;
		private HSSFWorkbook _wb;
		private EvalListener _EvalListener;

		public MySheet() {
			_wb = new HSSFWorkbook();
			_EvalListener = new EvalListener(_wb);
			_Evaluator = WorkbookEvaluatorTestHelper.CreateEvaluator(_wb, _EvalListener);
			_sheet = _wb.CreateSheet("Sheet1");
		}

		private static EvaluationCell wrapCell(HSSFCell cell) {
			return HSSFEvaluationTestHelper.wrapCell(cell);
		}

		public void SetCellValue(String cellRefText, double value) {
			HSSFCell cell = GetOrCreateCell(cellRefText);
			// be sure to blank cell, in case it is currently a formula
			cell.SetCellType(HSSFCell.CELL_TYPE_BLANK);
			// otherwise this line will only Set the formula cached result;
			cell.SetCellValue(value);
			_Evaluator.notifyUpdateCell(wrapCell(cell));
		}
		public void ClearCell(String cellRefText) {
			HSSFCell cell = GetOrCreateCell(cellRefText);
			cell.SetCellType(HSSFCell.CELL_TYPE_BLANK);
			_Evaluator.notifyUpdateCell(wrapCell(cell));
		}

		public void SetCellFormula(String cellRefText, String formulaText) {
			HSSFCell cell = GetOrCreateCell(cellRefText);
			cell.SetCellFormula(formulaText);
			_Evaluator.notifyUpdateCell(wrapCell(cell));
		}

		private HSSFCell GetOrCreateCell(String cellRefText) {
			CellReference cr = new CellReference(cellRefText);
			int rowIndex = cr.Row;
			HSSFRow row = _sheet.GetRow(rowIndex);
			if (row == null) {
				row = _sheet.CreateRow(rowIndex);
			}
			int cellIndex = cr.Col;
			HSSFCell cell = row.GetCell(cellIndex);
			if (cell == null) {
				cell = row.CreateCell(cellIndex);
			}
			return cell;
		}

		public ValueEval EvaluateCell(String cellRefText) {
			return _Evaluator.evaluate(wrapCell(getOrCreateCell(cellRefText)));
		}

		public String[] GetAndClearLog() {
			return _EvalListener.GetAndClearLog();
		}

		public void ClearAllCachedResultValues() {
			_Evaluator.clearAllCachedResultValues();
		}
	}

	private static MySheet CreateMediumComplex() {
		MySheet ms = new MySheet();

		// plain data in D1:F3
		ms.SetCellValue("D1", 12);
		ms.SetCellValue("E1", 13);
		ms.SetCellValue("D2", 14);
		ms.SetCellValue("E2", 15);
		ms.SetCellValue("D3", 16);
		ms.SetCellValue("E3", 17);


		ms.SetCellFormula("C1", "SUM(D1:E2)");
		ms.SetCellFormula("C2", "SUM(D2:E3)");
		ms.SetCellFormula("C3", "SUM(D3:E4)");

		ms.SetCellFormula("B1", "C2-C1");
		ms.SetCellFormula("B2", "B3*C1-C2");
		ms.SetCellValue("B3", 2);

		ms.SetCellFormula("A1", "MAX(B1:B2)");
		ms.SetCellFormula("A2", "MIN(B3,D2:F2)");
		ms.SetCellFormula("A3", "B3*C3");

		// clear all the logging from the above Initialisation
		ms.GetAndClearLog();
		ms.ClearAllCachedResultValues();
		return ms;
	}

	public void TestMediumComplex() {

		MySheet ms = CreateMediumComplex();
		// completely fresh Evaluation
		ConfirmEvaluate(ms, "A1", 46);
		ConfirmLog(ms, new String[] {
			"start A1 MAX(B1:B2)",
				"start B1 C2-C1",
					"start C2 SUM(D2:E3)",
						"value D2 14", "value E2 15", "value D3 16", "value E3 17",
					"end C2 62",
					"start C1 SUM(D1:E2)",
						"value D1 12", "value E1 13", "hit D2 14", "hit E2 15",
					"end C1 54",
				"end B1 8",
				"start B2 B3*C1-C2",
					"value B3 2",
					"hit C1 54",
					"hit C2 62",
				"end B2 46",
			"end A1 46",
		});


		// simple cache hit - immediate re-Evaluation with no Changes
		ConfirmEvaluate(ms, "A1", 46);
		ConfirmLog(ms, new String[] { "hit A1 46", });

		// change a low level cell
		ms.SetCellValue("D1", 10);
		ConfirmLog(ms, new String[] {
				"clear D1 10",
				"Clear1 C1 54",
				"Clear2 B1 8",
				"Clear3 A1 46",
				"Clear2 B2 46",
		});
		ConfirmEvaluate(ms, "A1", 42);
		ConfirmLog(ms, new String[] {
			"start A1 MAX(B1:B2)",
				"start B1 C2-C1",
					"hit C2 62",
					"start C1 SUM(D1:E2)",
						"hit D1 10", "hit E1 13", "hit D2 14", "hit E2 15",
					"end C1 52",
				"end B1 10",
				"start B2 B3*C1-C2",
					"hit B3 2",
					"hit C1 52",
					"hit C2 62",
				"end B2 42",
			"end A1 42",
		});

		// Reset and try changing an intermediate value
		ms = CreateMediumComplex();
		ConfirmEvaluate(ms, "A1", 46);
		ms.GetAndClearLog();

		ms.SetCellValue("B3", 3); // B3 is in the middle of the dependency tree
		ConfirmLog(ms, new String[] {
				"clear B3 3",
				"Clear1 B2 46",
				"Clear2 A1 46",
		});
		ConfirmEvaluate(ms, "A1", 100);
		ConfirmLog(ms, new String[] {
			"start A1 MAX(B1:B2)",
				"hit B1 8",
				"start B2 B3*C1-C2",
					"hit B3 3",
					"hit C1 54",
					"hit C2 62",
				"end B2 100",
			"end A1 100",
		});
	}

	public void TestMediumComplexWithDependencyChange() {

		// Changing an intermediate formula
		MySheet ms = CreateMediumComplex();
		ConfirmEvaluate(ms, "A1", 46);
		ms.GetAndClearLog();
		ms.SetCellFormula("B2", "B3*C2-C3"); // used to be "B3*C1-C2"
		ConfirmLog(ms, new String[] {
			"clear B2 46",
			"Clear1 A1 46",
		});

		ConfirmEvaluate(ms, "A1", 91);
		ConfirmLog(ms, new String[] {
			"start A1 MAX(B1:B2)",
				"hit B1 8",
				"start B2 B3*C2-C3",
					"hit B3 2",
					"hit C2 62",
					"start C3 SUM(D3:E4)",
						"hit D3 16", "hit E3 17",
//						"value D4 #BLANK#", "value E4 #BLANK#",
					"end C3 33",
				"end B2 91",
			"end A1 91",
		});

		//----------------
		// Note - From now on the demonstrated POI behaviour is not optimal
		//----------------

		// Now change a value that should no longer affect B2
		ms.SetCellValue("D1", 11);
		ConfirmLog(ms, new String[] {
			"clear D1 11",
			"Clear1 C1 54",
			// note there is no "Clear2 B2 91" here because B2 doesn't depend on C1 anymore
			"Clear2 B1 8",
			"Clear3 A1 91",
		});

		ConfirmEvaluate(ms, "B2", 91);
		ConfirmLog(ms, new String[] {
			"hit B2 91",  // further Confirmation that B2 was not Cleared due to changing D1 above
		});

		// things should be back to normal now
		ms.SetCellValue("D1", 11);
		ConfirmLog(ms, new String[] {  });
		ConfirmEvaluate(ms, "B2", 91);
		ConfirmLog(ms, new String[] {
			"hit B2 91",
		});
	}

	/**
	 * verifies that when updating a plain cell, depending (formula) cell cached values are Cleared
	 * only when the plain cell's value actually Changes
	 */
	public void TestRedundantUpdate() {
		MySheet ms = new MySheet();

		ms.SetCellValue("B1", 12);
		ms.SetCellValue("C1", 13);
		ms.SetCellFormula("A1", "B1+C1");

		// Evaluate twice to confirm caching looks OK
		ms.EvaluateCell("A1");
		ms.GetAndClearLog();
		ConfirmEvaluate(ms, "A1", 25);
		ConfirmLog(ms, new String[] {
			"hit A1 25",
		});

		// Make redundant update, and check re-Evaluation
		ms.SetCellValue("B1", 12); // value didn't change
		ConfirmLog(ms, new String[] {});
		ConfirmEvaluate(ms, "A1", 25);
		ConfirmLog(ms, new String[] {
			"hit A1 25",
		});

		ms.SetCellValue("B1", 11); // value changing
		ConfirmLog(ms, new String[] {
			"clear B1 11",
			"Clear1 A1 25",	// expect consuming formula cached result to Get Cleared
		});
		ConfirmEvaluate(ms, "A1", 24);
		ConfirmLog(ms, new String[] {
			"start A1 B1+C1",
			"hit B1 11",
			"hit C1 13",
			"end A1 24",
		});
	}

	/**
	 * Changing any input to a formula may cause the formula to 'use' a different Set of cells.
	 * Functions like INDEX and OFFSET make this effect obvious, with functions like MATCH
	 * and VLOOKUP the effect can be subtle.  The presence of error values can also produce this
	 * effect in almost every function and operator.
	 */
	public void TestSimpleWithDependencyChange() {

		MySheet ms = new MySheet();

		ms.SetCellFormula("A1", "INDEX(C1:E1,1,B1)");
		ms.SetCellValue("B1", 1);
		ms.SetCellValue("C1", 17);
		ms.SetCellValue("D1", 18);
		ms.SetCellValue("E1", 19);
		ms.ClearAllCachedResultValues();
		ms.GetAndClearLog();

		ConfirmEvaluate(ms, "A1", 17);
		ConfirmLog(ms, new String[] {
			"start A1 INDEX(C1:E1,1,B1)",
			"value B1 1",
			"value C1 17",
			"end A1 17",
		});
		ms.SetCellValue("B1", 2);
		ms.GetAndClearLog();

		ConfirmEvaluate(ms, "A1", 18);
		ConfirmLog(ms, new String[] {
			"start A1 INDEX(C1:E1,1,B1)",
			"hit B1 2",
			"value D1 18",
			"end A1 18",
		});

		// change C1. Note - last time A1 Evaluated C1 was not used
		ms.SetCellValue("C1", 15);
		ms.GetAndClearLog();
		ConfirmEvaluate(ms, "A1", 18);
		ConfirmLog(ms, new String[] {
			"hit A1 18",
		});

		// but A1 still uses D1, so if it Changes...
		ms.SetCellValue("D1", 25);
		ms.GetAndClearLog();
		ConfirmEvaluate(ms, "A1", 25);
		ConfirmLog(ms, new String[] {
			"start A1 INDEX(C1:E1,1,B1)",
			"hit B1 2",
			"hit D1 25",
			"end A1 25",
		});
	}

	public void TestBlankCells() {


		MySheet ms = new MySheet();

		ms.SetCellFormula("A1", "sum(B1:D4,B5:E6)");
		ms.SetCellValue("B1", 12);
		ms.ClearAllCachedResultValues();
		ms.GetAndClearLog();

		ConfirmEvaluate(ms, "A1", 12);
		ConfirmLog(ms, new String[] {
			"start A1 SUM(B1:D4,B5:E6)",
			"value B1 12",
			"end A1 12",
		});
		ms.SetCellValue("B6", 2);
		ms.GetAndClearLog();

		ConfirmEvaluate(ms, "A1", 14);
		ConfirmLog(ms, new String[] {
			"start A1 SUM(B1:D4,B5:E6)",
			"hit B1 12",
			"hit B6 2",
			"end A1 14",
		});
		ms.SetCellValue("E4", 2);
		ms.GetAndClearLog();

		ConfirmEvaluate(ms, "A1", 14);
		ConfirmLog(ms, new String[] {
			"hit A1 14",
		});

		ms.SetCellValue("D1", 1);
		ms.GetAndClearLog();

		ConfirmEvaluate(ms, "A1", 15);
		ConfirmLog(ms, new String[] {
			"start A1 SUM(B1:D4,B5:E6)",
			"hit B1 12",
			"hit D1 1",
			"hit B6 2",
			"end A1 15",
		});
	}

	/**
	 * Make sure that when blank cells are Changed to value/formula cells, any dependent formulas
	 * have their cached results Cleared.
	 */
	public void TestBlankCellChangedToValueCell_bug46053() {
		HSSFWorkbook wb = new HSSFWorkbook();
		HSSFSheet sheet = wb.CreateSheet("Sheet1");
		HSSFRow row = sheet.CreateRow(0);
		HSSFCell cellA1 = row.CreateCell(0);
		HSSFCell cellB1 = row.CreateCell(1);
		HSSFFormulaEvaluator fe = new HSSFFormulaEvaluator(wb);

		cellA1.SetCellFormula("B1+2.2");
		cellB1.SetCellValue(1.5);

		fe.NotifyUpdateCell(cellA1);
		fe.NotifyUpdateCell(cellB1);

		CellValue cv;
		cv = fe.Evaluate(cellA1);
		Assert.AreEqual(3.7, cv.GetNumberValue(), 0.0);

		cellB1.SetCellType(HSSFCell.CELL_TYPE_BLANK);
		fe.NotifyUpdateCell(cellB1);
		cv = fe.Evaluate(cellA1); // B1 was used to Evaluate A1
		Assert.AreEqual(2.2, cv.GetNumberValue(), 0.0);

		cellB1.SetCellValue(0.4);  // changing B1, so A1 cached result should be Cleared
		fe.NotifyUpdateCell(cellB1);
		cv = fe.Evaluate(cellA1);
		if (cv.GetNumberValue() == 2.2) {
			// looks like left-over cached result from before change to B1
			throw new AssertionFailedError("Identified bug 46053");
		}
		Assert.AreEqual(2.6, cv.GetNumberValue(), 0.0);
	}

	/**
	 * same use-case as the Test for bug 46053, but Checking trace values too
	 */
	public void TestBlankCellChangedToValueCell() {

		MySheet ms = new MySheet();

		ms.SetCellFormula("A1", "B1+2.2");
		ms.SetCellValue("B1", 1.5);
		ms.ClearAllCachedResultValues();
		ms.ClearCell("B1");
		ms.GetAndClearLog();

		ConfirmEvaluate(ms, "A1", 2.2);
		ConfirmLog(ms, new String[] {
			"start A1 B1+2.2",
			"end A1 2.2",
		});
		ms.SetCellValue("B1", 0.4);
		ConfirmLog(ms, new String[] {
			"ChangeFromBlank B1 0.4",
			"clear A1",
		});

		ConfirmEvaluate(ms, "A1", 2.6);
		ConfirmLog(ms, new String[] {
			"start A1 B1+2.2",
			"hit B1 0.4",
			"end A1 2.6",
		});
	}

	private static void ConfirmEvaluate(MySheet ms, String cellRefText, double expectedValue) {
		ValueEval v = ms.EvaluateCell(cellRefText);
		Assert.AreEqual(NumberEval.class, v.GetType());
		Assert.AreEqual(expectedValue, ((NumberEval)v).GetNumberValue(), 0.0);
	}

	private static void ConfirmLog(MySheet ms, String[] expectedLog) {
		String[] actualLog = ms.GetAndClearLog();
		int endIx = actualLog.Length;
		PrintStream ps = System.err;
		if (endIx != expectedLog.Length) {
			ps.println("Log lengths mismatch");
			dumpCompare(ps, expectedLog, actualLog);
			throw new AssertionFailedError("Log lengths mismatch");
		}
		for (int i=0; i< endIx; i++) {
			if (!actualLog[i].Equals(expectedLog[i])) {
				String msg = "Log entry mismatch at index " + i;
				ps.println(msg);
				dumpCompare(ps, expectedLog, actualLog);
				throw new AssertionFailedError(msg);
			}
		}

	}

	private static void dumpCompare(PrintStream ps, String[] expectedLog, String[] actualLog) {
		int max = Math.max(actualLog.Length, expectedLog.Length);
		ps.println("Index\tExpected\tActual");
		for(int i=0; i<max; i++) {
			ps.print(i + "\t");
			printItem(ps, expectedLog, i);
			ps.print("\t");
			printItem(ps, actualLog, i);
			ps.println();
		}
		ps.println();
		debugPrint(ps, actualLog);
	}

	private static void printItem(PrintStream ps, String[] ss, int index) {
		if (index < ss.Length) {
			ps.print(ss[index]);
		}
	}

	private static void debugPrint(PrintStream ps, String[] log) {
		for (int i = 0; i < log.Length; i++) {
			ps.println('"' + log[i] + "\",");
		}
	}

    private static void TestPlainValueCache(Workbook wb, int numberOfSheets) {

        Row row;
        Cell cell;

        //create summary sheet
        Sheet summary = wb.CreateSheet("summary");
        wb.SetActiveSheet(wb.GetSheetIndex(summary));

        //formula referring all sheets Created below
        row = summary.CreateRow(0);
        Cell summaryCell = row.CreateCell(0);
        summaryCell.SetCellFormula("SUM(A2:A" + (numberOfSheets + 2) + ")");


        //create sheets with cells having (different) numbers
        // and add a row to summary
        for (int i = 1; i < numberOfSheets; i++) {
            Sheet sheet = wb.CreateSheet("new" + i);

            row = sheet.CreateRow(0);
            cell = row.CreateCell(0);
            cell.SetCellValue(i);

            row = summary.CreateRow(i);
            cell = row.CreateCell(0);
            cell.SetCellFormula("new" + i + "!A1");

        }


        //calculate
        FormulaEvaluator Evaluator = wb.GetCreationHelper().CreateFormulaEvaluator();
        Evaluator.evaluateFormulaCell(summaryCell);
    }


    public void TestPlainValueCache()  {

        Workbook wb = new HSSFWorkbook();
        int numberOfSheets = 4098; // Bug 51448 reported that  Evaluation Cache got messed up After 256 sheets

        Row row;
        Cell cell;

        //create summary sheet
        Sheet summary = wb.CreateSheet("summary");
        wb.SetActiveSheet(wb.GetSheetIndex(summary));

        //formula referring all sheets Created below
        row = summary.CreateRow(0);
        Cell summaryCell = row.CreateCell(0);
        summaryCell.SetCellFormula("SUM(A2:A" + (numberOfSheets + 2) + ")");


        //create sheets with cells having (different) numbers
        // and add a row to summary
        for (int i = 1; i < numberOfSheets; i++) {
            Sheet sheet = wb.CreateSheet("new" + i);

            row = sheet.CreateRow(0);
            cell = row.CreateCell(0);
            cell.SetCellValue(i);

            row = summary.CreateRow(i);
            cell = row.CreateCell(0);
            cell.SetCellFormula("new" + i + "!A1");

        }


        //calculate
        FormulaEvaluator Evaluator = wb.GetCreationHelper().CreateFormulaEvaluator();
        Evaluator.evaluateFormulaCell(summaryCell);
        Assert.AreEqual(8394753.0, summaryCell.GetNumericCellValue());
    }

}

