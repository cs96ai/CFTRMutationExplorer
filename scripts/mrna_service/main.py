"""
FastAPI service for mRNA therapy optimization.
Exposes REST API for the WPF frontend to start/stop optimization,
poll progress, and fetch results.

Run: uvicorn main:app --host 127.0.0.1 --port 8787
"""

import asyncio
import json as json_mod
import threading
import traceback
import sys
from datetime import datetime
from collections import deque
from fastapi import FastAPI, HTTPException, WebSocket, WebSocketDisconnect
from starlette.types import ASGIApp, Receive, Scope, Send
from pydantic import BaseModel
from typing import Optional
from pathlib import Path

from optimizer import run_optimization, state, set_log_fn, set_status_fn
from persistence import list_runs, load_checkpoint

app = FastAPI(title="CFTR mRNA Therapy Optimizer", version="1.0.0")


class LocalhostMiddleware:
    """
    Lightweight middleware for local desktop apps.
    Starlette's CORSMiddleware rejects WebSocket upgrades that lack an Origin
    header (which .NET ClientWebSocket never sends).  Since this service only
    talks to a local WPF app over 127.0.0.1, we skip CORS entirely and just
    allow everything.
    """
    def __init__(self, app: ASGIApp):
        self.app = app

    async def __call__(self, scope: Scope, receive: Receive, send: Send):
        await self.app(scope, receive, send)


app.add_middleware(LocalhostMiddleware)

# In-memory log buffer for debug output
_log_buffer: deque[dict] = deque(maxlen=500)
_log_lock = threading.Lock()

# WebSocket broadcast: thread-safe queue feeds an asyncio broadcaster
import queue as stdlib_queue

_ws_clients: dict[WebSocket, asyncio.Queue] = {}
_ws_lock = threading.Lock()
_thread_queue = stdlib_queue.Queue(maxsize=2000)


def _push_ws_event(event: dict):
    """Thread-safe push from optimizer thread into the broadcast queue."""
    try:
        _thread_queue.put_nowait(event)
    except stdlib_queue.Full:
        pass


def add_log(level: str, message: str):
    """Add a timestamped log entry to the buffer and push to WebSocket clients."""
    entry = {
        "timestamp": datetime.now().isoformat(),
        "level": level,
        "message": message,
    }
    with _log_lock:
        _log_buffer.append(entry)
    print(f"[{entry['timestamp']}] [{level}] {message}", flush=True)
    _push_ws_event({"type": "log", **entry})


# Capture GPU info at startup
def _log_startup():
    add_log("INFO", "Python mRNA optimization service starting...")
    add_log("INFO", f"Python version: {sys.version}")
    try:
        import torch
        add_log("INFO", f"PyTorch version: {torch.__version__}")
        if torch.cuda.is_available():
            gpu_name = torch.cuda.get_device_name(0)
            vram = torch.cuda.get_device_properties(0).total_memory / 1e9
            add_log("INFO", f"CUDA available: True")
            add_log("INFO", f"GPU: {gpu_name}")
            add_log("INFO", f"VRAM: {vram:.1f} GB")
        else:
            add_log("WARN", "CUDA not available — running on CPU (will be slow)")
    except ImportError:
        add_log("ERROR", "PyTorch not installed")
    except Exception as e:
        add_log("ERROR", f"GPU detection error: {e}")


_log_startup()
set_log_fn(add_log)


def _on_status_change():
    """Called by the optimizer thread after each generation update."""
    with state.lock:
        snapshot = {
            "type": "status",
            "running": state.running,
            "generation": state.generation,
            "best_fitness": state.best_fitness,
            "avg_fitness": state.avg_fitness,
            "pareto_front_size": state.pareto_front_size,
            "seqs_per_sec": state.seqs_per_sec,
            "status": state.status,
            "run_id": state.run_id,
            "threshold_reached": state.threshold_reached,
            "started_at": state.started_at,
        }
    _push_ws_event(snapshot)


set_status_fn(_on_status_change)

RESULTS_DIR = str(Path(__file__).parent / "results")

_optimizer_thread: Optional[threading.Thread] = None


class OptimizeRequest(BaseModel):
    population_size: int = 750
    crossover_rate: float = 0.75
    mutation_rate: float = 0.03
    fitness_threshold: float = 0.90
    checkpoint_interval: int = 100
    tournament_size: int = 2
    weights: list[float] = [1.0, 0.9, 0.9, 0.7, 0.6, 0.5, 0.4]
    resume_from: Optional[str] = None


class StatusResponse(BaseModel):
    running: bool
    generation: int
    best_fitness: float
    avg_fitness: float
    pareto_front_size: int
    seqs_per_sec: int
    status: str
    run_id: str
    threshold_reached: bool
    started_at: Optional[str] = None


@app.get("/")
def root():
    return {"service": "CFTR mRNA Therapy Optimizer", "status": "ready"}


@app.post("/optimize/start")
def start_optimization(req: OptimizeRequest):
    global _optimizer_thread

    with state.lock:
        if state.running:
            add_log("WARN", "Start rejected: optimization already running")
            raise HTTPException(400, "Optimization already running")

    add_log("INFO", f"Starting optimization: pop={req.population_size}, threshold={req.fitness_threshold}, checkpoint_every={req.checkpoint_interval}")
    add_log("INFO", f"Weights: {req.weights}")

    def _run_with_logging(**kwargs):
        try:
            add_log("INFO", "Optimizer thread started, initializing GPU scorer...")
            run_optimization(**kwargs)
            add_log("INFO", "Optimizer thread finished normally")
        except Exception as e:
            add_log("ERROR", f"Optimizer crashed: {type(e).__name__}: {e}")
            add_log("ERROR", traceback.format_exc())
            with state.lock:
                state.running = False
                state.status = f"CRASHED: {e}"

    _optimizer_thread = threading.Thread(
        target=_run_with_logging,
        kwargs={
            "population_size": req.population_size,
            "crossover_rate": req.crossover_rate,
            "mutation_rate": req.mutation_rate,
            "fitness_threshold": req.fitness_threshold,
            "checkpoint_interval": req.checkpoint_interval,
            "tournament_size": req.tournament_size,
            "weights": req.weights,
            "results_dir": RESULTS_DIR,
            "resume_from": req.resume_from,
        },
        daemon=True,
    )
    _optimizer_thread.start()
    return {"message": "Optimization started", "run_id": state.run_id or "starting..."}


@app.post("/optimize/stop")
def stop_optimization():
    with state.lock:
        if not state.running:
            add_log("INFO", "Stop requested but not running")
            return {"message": "Not running"}
        state.running = False
    add_log("INFO", "Stop signal sent — optimizer will save checkpoint and halt")
    return {"message": "Stop signal sent. Will save checkpoint and halt."}


@app.get("/optimize/status", response_model=StatusResponse)
def get_status():
    with state.lock:
        return StatusResponse(
            running=state.running,
            generation=state.generation,
            best_fitness=state.best_fitness,
            avg_fitness=state.avg_fitness,
            pareto_front_size=state.pareto_front_size,
            seqs_per_sec=state.seqs_per_sec,
            status=state.status,
            run_id=state.run_id,
            threshold_reached=state.threshold_reached,
            started_at=state.started_at,
        )


@app.get("/optimize/results")
def get_results():
    with state.lock:
        return {
            "run_id": state.run_id,
            "generation": state.generation,
            "best_fitness": state.best_fitness,
            "pareto_front": state.pareto_front,
            "best_candidate": state.best_candidate,
            "history": state.history[-200:],
        }


@app.get("/optimize/history")
def get_history():
    """List all saved optimization runs."""
    runs = list_runs(RESULTS_DIR)
    return {"runs": runs}


@app.get("/optimize/history/{run_id}")
def load_run(run_id: str):
    """Load a specific saved run."""
    results_path = Path(RESULTS_DIR)

    # Try FINAL first, then latest checkpoint
    for pattern in [f"optimization_{run_id}_FINAL.json", f"optimization_{run_id}_gen*.json"]:
        files = sorted(results_path.glob(pattern), reverse=True)
        if files:
            data = load_checkpoint(str(files[0]))
            data.pop("population", None)  # don't send the full population over API
            return data

    raise HTTPException(404, f"Run {run_id} not found")


@app.post("/optimize/resume/{run_id}")
def resume_run(run_id: str, req: OptimizeRequest):
    """Resume a previously saved optimization run."""
    global _optimizer_thread

    with state.lock:
        if state.running:
            raise HTTPException(400, "Optimization already running")

    results_path = Path(RESULTS_DIR)
    checkpoint_file = None

    for pattern in [f"optimization_{run_id}_FINAL.json", f"optimization_{run_id}_gen*.json"]:
        files = sorted(results_path.glob(pattern), reverse=True)
        if files:
            checkpoint_file = str(files[0])
            break

    if not checkpoint_file:
        add_log("ERROR", f"Resume failed: no checkpoint for run {run_id}")
        raise HTTPException(404, f"No checkpoint found for run {run_id}")

    add_log("INFO", f"Resuming run {run_id} from {checkpoint_file}")

    def _run_with_logging(**kwargs):
        try:
            add_log("INFO", f"Resume thread started for {run_id}")
            run_optimization(**kwargs)
            add_log("INFO", "Resume thread finished normally")
        except Exception as e:
            add_log("ERROR", f"Optimizer crashed on resume: {type(e).__name__}: {e}")
            add_log("ERROR", traceback.format_exc())
            with state.lock:
                state.running = False
                state.status = f"CRASHED: {e}"

    _optimizer_thread = threading.Thread(
        target=_run_with_logging,
        kwargs={
            "population_size": req.population_size,
            "crossover_rate": req.crossover_rate,
            "mutation_rate": req.mutation_rate,
            "fitness_threshold": req.fitness_threshold,
            "checkpoint_interval": req.checkpoint_interval,
            "tournament_size": req.tournament_size,
            "weights": req.weights,
            "results_dir": RESULTS_DIR,
            "resume_from": checkpoint_file,
        },
        daemon=True,
    )
    _optimizer_thread.start()
    return {"message": f"Resumed run {run_id}", "checkpoint": checkpoint_file}


@app.get("/logs")
def get_logs(since: Optional[str] = None, limit: int = 200):
    """Return recent log entries. Optionally filter by timestamp."""
    with _log_lock:
        entries = list(_log_buffer)
    if since:
        entries = [e for e in entries if e["timestamp"] > since]
    return {"logs": entries[-limit:], "total": len(entries)}


async def _ws_broadcaster():
    """Background task: drains _thread_queue and fans out to all connected WS clients."""
    while True:
        try:
            event = _thread_queue.get_nowait()
            dead: list[WebSocket] = []
            with _ws_lock:
                clients = list(_ws_clients.items())
            for ws, q in clients:
                try:
                    q.put_nowait(event)
                except asyncio.QueueFull:
                    dead.append(ws)
            if dead:
                with _ws_lock:
                    for ws in dead:
                        _ws_clients.pop(ws, None)
        except stdlib_queue.Empty:
            await asyncio.sleep(0.05)
        except Exception:
            await asyncio.sleep(0.1)


@app.on_event("startup")
async def _start_broadcaster():
    asyncio.create_task(_ws_broadcaster())


@app.websocket("/ws")
async def websocket_endpoint(ws: WebSocket):
    """
    WebSocket that streams status updates and log entries to the WPF client.
    Each message is a JSON object with a "type" field: "status" or "log".
    """
    await ws.accept()
    add_log("INFO", "WebSocket client connected")
    client_queue: asyncio.Queue = asyncio.Queue(maxsize=500)
    with _ws_lock:
        _ws_clients[ws] = client_queue

    # Send current state snapshot immediately so the client doesn't start blank
    with state.lock:
        snapshot = {
            "type": "status",
            "running": state.running,
            "generation": state.generation,
            "best_fitness": state.best_fitness,
            "avg_fitness": state.avg_fitness,
            "pareto_front_size": state.pareto_front_size,
            "seqs_per_sec": state.seqs_per_sec,
            "status": state.status,
            "run_id": state.run_id,
            "threshold_reached": state.threshold_reached,
            "started_at": state.started_at,
        }
    await ws.send_text(json_mod.dumps(snapshot))

    try:
        while True:
            try:
                event = await asyncio.wait_for(client_queue.get(), timeout=5.0)
                await ws.send_text(json_mod.dumps(event))
            except asyncio.TimeoutError:
                await ws.send_text(json_mod.dumps({"type": "heartbeat"}))
    except WebSocketDisconnect:
        pass
    except Exception as e:
        add_log("WARN", f"WebSocket error: {e}")
    finally:
        with _ws_lock:
            _ws_clients.pop(ws, None)
        add_log("INFO", "WebSocket client disconnected")


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="127.0.0.1", port=8787)
