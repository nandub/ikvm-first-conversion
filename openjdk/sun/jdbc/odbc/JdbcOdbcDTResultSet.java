/*
  Copyright (C) 2009 Volker Berlin (i-net software)

  This software is provided 'as-is', without any express or implied
  warranty.  In no event will the authors be held liable for any damages
  arising from the use of this software.

  Permission is granted to anyone to use this software for any purpose,
  including commercial applications, and to alter it and redistribute it
  freely, subject to the following restrictions:

  1. The origin of this software must not be misrepresented; you must not
     claim that you wrote the original software. If you use this software
     in a product, an acknowledgment in the product documentation would be
     appreciated but is not required.
  2. Altered source versions must be plainly marked as such, and must not be
     misrepresented as being the original software.
  3. This notice may not be removed or altered from any source distribution.

  Jeroen Frijters
  jeroen@frijters.net
  
 */
package sun.jdbc.odbc;

import java.sql.*;

import cli.System.Data.*;

/**
 * This JDBC Driver is a wrapper to the ODBC.NET Data Provider. This ResultSet based on DataTable.
 * It is read only and scrollable.
 */
public class JdbcOdbcDTResultSet extends JdbcOdbcResultSet{

    private final DataTable data;

    private DataRowCollection rows;

    private int rowIndex; // row index starting with 0; -1 means beforeFirst

    private cli.System.Data.DataRow row;


    public JdbcOdbcDTResultSet(DataTable data){
        super(null, TYPE_SCROLL_INSENSITIVE, CONCUR_READ_ONLY);
        this.data = data;
        this.rows = data.get_Rows();
        this.rowIndex = -1;
    }


    @Override
    public boolean absolute(int rowPosition) throws SQLException{
        if(rowPosition == 0){
            return !isBeforeFirst() && !isAfterLast();
        }
        DataRowCollection dataRows = getRows();
        int count = dataRows.get_Count();
        if(rowPosition > 0){
            if(rowPosition > count){
                row = null;
                rowIndex = count;
                return false;
            }
            rowIndex = rowPosition - 1;
            row = dataRows.get_Item(rowIndex);
            return true;
        }else{
            if(-rowPosition > count){
                row = null;
                rowIndex = -1;
                return false;
            }
            rowIndex = count + rowPosition;
            row = dataRows.get_Item(rowIndex);
            return true;
        }
    }


    @Override
    public void afterLast() throws SQLException{
        row = null;
        rowIndex = getRows().get_Count();
    }


    @Override
    public void beforeFirst(){
        row = null;
        rowIndex = -1;
    }


    @Override
    public void close(){
        rows = null;
    }


    @Override
    public int findColumn(String columnLabel) throws SQLException{
        getRows(); // Check if ResultSet is closed
        int idx = data.get_Columns().IndexOf(columnLabel);
        if(idx < 0){
            throw new SQLException("Column '" + columnLabel + "' not found.", "S0022");
        }
        return idx + 1;
    }


    @Override
    public boolean first() throws SQLException{
        beforeFirst();
        return next();
    }


    @Override
    public ResultSetMetaData getMetaData(){
        return new JdbcOdbcDTResultSetMetaData(data);
    }


    @Override
    public int getRow() throws SQLException{
        return rowIndex < getRows().get_Count() ? rowIndex + 1 : 0;
    }


    @Override
    public boolean isAfterLast() throws SQLException{
        int count = getRows().get_Count();
        return rowIndex >= count || count == 0;
    }


    @Override
    public boolean isBeforeFirst() throws SQLException{
        return rowIndex <= -1 || getRows().get_Count() == 0;
    }


    @Override
    public boolean isClosed(){
        return rows == null;
    }


    @Override
    public boolean isFirst() throws SQLException{
        return rowIndex == 0 && getRows().get_Count() > 0;
    }


    @Override
    public boolean isLast() throws SQLException{
        return rowIndex >= 0 && rowIndex == getRows().get_Count() - 1;
    }


    @Override
    public boolean last() throws SQLException{
        afterLast();
        return previous();
    }


    @Override
    public boolean next() throws SQLException{
        DataRowCollection dataRows = getRows();
        if(rowIndex + 1 < dataRows.get_Count()){
            row = dataRows.get_Item(++rowIndex);
            return true;
        }else{
            rowIndex = dataRows.get_Count();
            row = null;
            return false;
        }
    }


    @Override
    public boolean previous() throws SQLException{
        if(rowIndex > 0){
            row = getRows().get_Item(--rowIndex);
            return true;
        }else{
            rowIndex = -1;
            row = null;
            return false;
        }
    }


    @Override
    public void refreshRow(){
        // ignore it
    }


    @Override
    public boolean relative(int rowPositions) throws SQLException{
        DataRowCollection dataRows = getRows();
        int newRowIndex = rowIndex + rowPositions;
        if(newRowIndex < 0){
            rowIndex = -1;
            row = null;
            return false;
        }
        int count = dataRows.get_Count();
        if(newRowIndex >= dataRows.get_Count()){
            rowIndex = count;
            row = null;
            return false;
        }
        rowIndex = newRowIndex;
        row = getRows().get_Item(newRowIndex);
        return true;
    }


    /**
     * {@inheritDoc}
     */
    @Override
    protected Object getObjectImpl(int columnIndex) throws SQLException{
        getRows(); // checks if ResultSet is closed
        if(row == null){
            throw new SQLException("No current row", "S1109");
        }
        try{
            return row.get_Item(columnIndex - 1);
        }catch(ArrayIndexOutOfBoundsException ex){
            throw new SQLException( "Invalid column number ("+columnIndex+"). A number between 1 and "+data.get_Columns().get_Count()+" is valid.", "S1002");
        }
    }


    /**
     * Check if this ResultSet is closed before access to the DataRowCollection
     * 
     * @return the local rows object
     * @throws SQLException
     *             If the ResultSet is closed.
     */
    private DataRowCollection getRows() throws SQLException{
        if(rows == null){
            throw new SQLException("ResultSet is closed.", "24000");
        }
        return rows;
    }
}
