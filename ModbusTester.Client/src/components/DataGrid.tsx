import { useState } from "react";
import { cn } from "../lib/cn";
import { decodeBits, decodeRegisters, type DecodedRow } from "../lib/registerDecoder";
import type { ModbusDataSnapshot } from "../types/modbus";

const ROWS_PER_COLUMN = 10;

function chunk<T>(items: T[], size: number): T[][] {
  const groups: T[][] = [];
  for (let i = 0; i < items.length; i += size) groups.push(items.slice(i, i + size));
  return groups;
}

interface RegisterCellProps {
  row: DecodedRow;
  isSelected: boolean;
  writable: boolean;
  onClick: () => void;
  onCommit: (value: string) => void;
}

/**
 * Single click selects; double-click (when the value is a single writable 16-bit register or a
 * coil) opens inline editing — Enter/blur commits via the Write REST endpoint, Escape cancels.
 */
function RegisterCell({ row, isSelected, writable, onClick, onCommit }: RegisterCellProps) {
  const [editValue, setEditValue] = useState<string | null>(null);

  function commit() {
    if (editValue !== null && editValue.trim() !== "") onCommit(editValue.trim());
    setEditValue(null);
  }

  return (
    <div
      onClick={onClick}
      onDoubleClick={() => writable && setEditValue(String(row.text))}
      className={cn(
        "flex h-[30px] items-center gap-2 border-b border-border px-2 transition-colors",
        writable ? "cursor-pointer" : "cursor-default",
        isSelected ? "z-10 bg-accent/5 ring-1 ring-accent" : "hover:bg-surface-hover",
      )}
    >
      <div className="w-8 flex-shrink-0 select-none text-right font-mono text-[12px] text-dim-foreground">
        {row.address}
      </div>
      {editValue !== null ? (
        <input
          autoFocus
          value={editValue}
          onChange={(e) => setEditValue(e.target.value)}
          onBlur={commit}
          onKeyDown={(e) => {
            if (e.key === "Enter") commit();
            if (e.key === "Escape") setEditValue(null);
          }}
          onClick={(e) => e.stopPropagation()}
          className="flex-1 border border-accent bg-panel px-1 font-mono text-[13px] font-medium text-foreground outline-none"
        />
      ) : (
        <div className="flex-1 truncate font-mono text-[13px] font-medium text-foreground">{row.text}</div>
      )}
    </div>
  );
}

function RegisterColumn({
  rows,
  selectedAddress,
  writable,
  onSelect,
  onCommit,
}: {
  rows: DecodedRow[];
  selectedAddress: number | null;
  writable: boolean;
  onSelect: (address: number) => void;
  onCommit: (address: number, value: string) => void;
}) {
  return (
    <div className="flex w-[260px] flex-shrink-0 flex-col overflow-hidden rounded border border-border bg-panel">
      <div className="flex h-[34px] items-center justify-center border-b border-border bg-surface-hover">
        <span className="font-mono text-[12px] font-medium text-muted-foreground">{rows[0]?.address}</span>
      </div>
      <div className="flex flex-col">
        {rows.map((row) => (
          <RegisterCell
            key={row.address}
            row={row}
            isSelected={selectedAddress === row.address}
            writable={writable}
            onClick={() => onSelect(row.address)}
            onCommit={(value) => onCommit(row.address, value)}
          />
        ))}
      </div>
    </div>
  );
}

interface DataGridProps {
  snapshot: ModbusDataSnapshot | null;
  isBitBased: boolean;
  onWriteRegister: (address: number, value: number) => void;
  onWriteCoil: (address: number, value: boolean) => void;
}

export function DataGrid({ snapshot, isBitBased, onWriteRegister, onWriteCoil }: DataGridProps) {
  const [selectedAddress, setSelectedAddress] = useState<number | null>(null);

  const rows: DecodedRow[] = snapshot ? (isBitBased ? decodeBits(snapshot) : decodeRegisters(snapshot)) : [];
  const columns = chunk(rows, ROWS_PER_COLUMN);

  // Multi-register decoded values (Long/Float/Double) can't be safely round-tripped from a
  // single edited string back into N registers, so inline write is only offered for coils and
  // plain single-register values.
  const writable = isBitBased || snapshot?.registerSizePerItem === 1;

  function handleCommit(address: number, value: string) {
    if (isBitBased) {
      onWriteCoil(address, value === "1" || value.toLowerCase() === "true");
    } else {
      const parsed = Number(value);
      if (!Number.isNaN(parsed)) onWriteRegister(address, parsed);
    }
  }

  return (
    <div className="relative flex flex-1 flex-col overflow-hidden bg-background">
      {columns.length === 0 ? (
        <div className="flex flex-1 items-center justify-center text-[13px] text-dim-foreground">
          Waiting for data...
        </div>
      ) : (
        <div className="scrollbar-industrial flex h-full flex-col flex-wrap content-start gap-4 overflow-x-auto p-4">
          {columns.map((columnRows, i) => (
            <RegisterColumn
              key={columnRows[0]?.address ?? i}
              rows={columnRows}
              selectedAddress={selectedAddress}
              writable={writable}
              onSelect={setSelectedAddress}
              onCommit={handleCommit}
            />
          ))}
        </div>
      )}
    </div>
  );
}
