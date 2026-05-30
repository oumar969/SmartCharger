import { useState, useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  ComposedChart, Bar, Line, XAxis, YAxis, Tooltip, CartesianGrid,
  Cell, ResponsiveContainer, ReferenceLine, Legend
} from "recharts";
import axios from "axios";
import "./App.css";

interface HourData {
  hourStart: string;
  priceDKK: number;
  co2PerKwh: number;
  priceArea: string;
}

interface ChargeRecommendation {
  hourStart: string;
  priceDKK: number;
  co2PerKwh: number;
  isRecommended: boolean;
}

interface ChargeWindow {
  windowStart: string;
  windowEnd: string;
  totalCostDKK: number;
  averagePriceDKK: number;
  averageCo2: number;
  hours: ChargeRecommendation[];
}

type Strategy = "Cheapest" | "Greenest";

const API   = "http://localhost:5000/api/elspot";
const AREAS = ["DK1", "DK2"];

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const fetchJson = (url: string): Promise<any> => axios.get(url).then(r => r.data);
const fmt   = (iso: string) =>
  new Date(iso).toLocaleTimeString("da-DK", { hour: "2-digit", minute: "2-digit" });

export default function App() {
  const [hours,    setHours]    = useState(4);
  const [area,     setArea]     = useState<string>("DK2");
  const [deadline, setDeadline] = useState("07:00");
  const [strategy, setStrategy] = useState<Strategy>("Cheapest");

  const deadlineISO = useMemo(() => {
    const parts = deadline.split(":");
    const h = parseInt(parts[0] ?? "", 10);
    const m = parseInt(parts[1] ?? "", 10);
    const d = new Date();
    d.setDate(d.getDate() + 1);
    d.setHours(isNaN(h) ? 7 : h, isNaN(m) ? 0 : m, 0, 0);
    return d.toISOString();
  }, [deadline]);

  const { data: recommendations = [], isLoading, isError } = useQuery<ChargeRecommendation[]>({
    queryKey: ["recommendations", hours, area, strategy],
    queryFn:  () => fetchJson(`${API}/recommendations?hours=${hours}&area=${area}&strategy=${strategy}`),
  });

  const { data: merged = [] } = useQuery<HourData[]>({
    queryKey: ["merged", area],
    queryFn:  () => fetchJson(`${API}/merged?area=${area}`),
  });

  const { data: window } = useQuery<ChargeWindow | null>({
    queryKey: ["window", hours, area, deadlineISO, strategy],
    retry: false,
    queryFn:  () =>
      axios.get<ChargeWindow>(`${API}/window?hours=${hours}&area=${area}&deadline=${encodeURIComponent(deadlineISO)}&strategy=${strategy}`)
        .then(r => r.data)
        .catch(e => e?.response?.status === 404 ? null : Promise.reject(e)),
  });

  const avgPrice = merged.length ? merged.reduce((s, d) => s + d.priceDKK, 0) / merged.length : 0;
  const avgCo2   = merged.length ? merged.reduce((s, d) => s + d.co2PerKwh, 0) / merged.length : 0;

  const chartData = merged.map(h => {
    const rec = recommendations.find(r => r.hourStart === h.hourStart);
    const inWin = window &&
      new Date(h.hourStart) >= new Date(window.windowStart) &&
      new Date(h.hourStart) < new Date(window.windowEnd);
    return { ...h, isRecommended: rec?.isRecommended ?? false, inWindow: inWin ?? false };
  });

  const isCheapest = strategy === "Cheapest";

  return (
    <div className="app">
      <header>
        <h1>⚡ SmartCharger</h1>
        <p>Opladningsanbefaling baseret på live elpriser og CO₂ fra Energinet</p>
      </header>

      <div className="strategy-toggle">
        <button
          className={isCheapest ? "active" : ""}
          onClick={() => setStrategy("Cheapest")}>
          💰 Billigst
        </button>
        <button
          className={!isCheapest ? "active green" : "green"}
          onClick={() => setStrategy("Greenest")}>
          🌿 Grønnest
        </button>
      </div>

      <div className="controls">
        <label>
          Prisområde:
          <select value={area} onChange={e => setArea(e.target.value)}>
            {AREAS.map(a => <option key={a}>{a}</option>)}
          </select>
        </label>
        <label>
          Timer der skal oplades:
          <input type="number" min={1} max={12} value={hours}
            onChange={e => setHours(Number(e.target.value))} />
        </label>
        <label>
          Bilen skal køre kl.:
          <input type="time" value={deadline} onChange={e => setDeadline(e.target.value)} />
        </label>
      </div>

      {isError && <p className="error">Kunne ikke hente data — prøv igen om lidt.</p>}

      {isLoading ? (
        <p className="loading">Henter priser…</p>
      ) : (
        <>
          {window && (
            <div className={`window-banner ${!isCheapest ? "green-banner" : ""}`}>
              <strong>Bedste {isCheapest ? "billigste" : "grønneste"} vindue:</strong>{" "}
              {fmt(window.windowStart)} – {fmt(window.windowEnd)}{" · "}
              {isCheapest
                ? `gns. ${window.averagePriceDKK.toFixed(3)} kr/kWh · total ${window.totalCostDKK.toFixed(3)} kr/kWh`
                : `gns. ${window.averageCo2.toFixed(0)} g CO₂/kWh`}
            </div>
          )}

          <div className="summary">
            <div className="card">
              <span>{avgPrice.toFixed(2)} kr</span>
              <small>Gns. spotpris</small>
            </div>
            <div className="card green">
              <span>{avgCo2.toFixed(0)} g</span>
              <small>Gns. CO₂/kWh</small>
            </div>
            {window && (
              <div className={`card ${isCheapest ? "blue" : "emerald"}`}>
                <span>{fmt(window.windowStart)}</span>
                <small>Start optimalt vindue</small>
              </div>
            )}
          </div>

          <ResponsiveContainer width="100%" height={360}>
            <ComposedChart data={chartData} margin={{ top: 8, right: 50, left: 10, bottom: 70 }}>
              <CartesianGrid strokeDasharray="3 3" />
              <XAxis
                dataKey="hourStart"
                tickFormatter={fmt}
                angle={-45}
                textAnchor="end"
                interval={3}
                height={60}
              />
              <YAxis
                yAxisId="price"
                tickFormatter={v => `${v} kr`}
                width={60}
              />
              <YAxis
                yAxisId="co2"
                orientation="right"
                tickFormatter={v => `${v} g`}
                domain={["auto", "auto"]}
                width={55}
              />
              <Tooltip
                formatter={(v, name) => {
                  const val = Number(v);
                  return name === "CO₂"
                    ? [`${val.toFixed(0)} g CO₂/kWh`, "CO₂"]
                    : [`${val.toFixed(4)} kr/kWh`, "Spotpris"];
                }}
                labelFormatter={l => new Date(l).toLocaleString("da-DK")}
              />
              <Legend verticalAlign="bottom" height={36} wrapperStyle={{ paddingTop: "12px" }} />
              <ReferenceLine yAxisId="price" y={avgPrice} stroke="#888" strokeDasharray="4 4" />
              <Bar yAxisId="price" dataKey="priceDKK" radius={[4, 4, 0, 0]} name="Spotpris">
                {chartData.map((entry, i) => (
                  <Cell key={i} fill={
                    entry.inWindow      ? (isCheapest ? "#22c55e" : "#10b981") :
                    entry.isRecommended ? "#86efac" : "#64748b"
                  } />
                ))}
              </Bar>
              <Line yAxisId="co2" type="monotone" dataKey="co2PerKwh"
                stroke="#f59e0b" strokeWidth={2} dot={false} name="CO₂" />
            </ComposedChart>
          </ResponsiveContainer>

          <p className="legend">
            <span className="dot green" /> Optimalt vindue &nbsp;
            <span className="dot lightgreen" /> Anbefalet time &nbsp;
            <span className="dot grey" /> Øvrige timer
          </p>
        </>
      )}
    </div>
  );
}
