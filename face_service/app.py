"""Servicio de reconocimiento facial de SIGELAB.

Recibe una foto y devuelve el vector (embedding) del rostro.
Solo debe escuchar en 127.0.0.1: unicamente el backend .NET lo llama.
Modelos: YuNet (deteccion) y SFace (reconocimiento) de OpenCV Zoo.
"""
import os
import threading

import cv2
import numpy as np
from fastapi import FastAPI, File, HTTPException, UploadFile

MODEL_DIR = os.environ.get("MODEL_DIR", "/opt/sigelab-face/models")
DET_MODEL = os.path.join(MODEL_DIR, "face_detection_yunet_2023mar.onnx")
REC_MODEL = os.path.join(MODEL_DIR, "face_recognition_sface_2021dec.onnx")

MAX_BYTES = 8 * 1024 * 1024   # tamano maximo de la imagen
MAX_SIDE = 1280               # se reduce si el lado mayor excede este valor
MIN_SCORE = 0.85              # confianza minima del detector (0 a 1)
MIN_FACE_PX = 80              # ancho minimo del rostro en pixeles

detector = cv2.FaceDetectorYN.create(DET_MODEL, "", (320, 320), MIN_SCORE, 0.3, 5000)
recognizer = cv2.FaceRecognizerSF.create(REC_MODEL, "")
lock = threading.Lock()  # el detector no es seguro entre hilos

app = FastAPI(title="SIGELAB face service")


@app.get("/salud")
def salud():
    return {"ok": True}


@app.post("/embedding")
def embedding(foto: UploadFile = File(...)):
    datos = foto.file.read(MAX_BYTES + 1)
    if len(datos) > MAX_BYTES:
        raise HTTPException(status_code=413, detail="La imagen es demasiado grande.")

    img = cv2.imdecode(np.frombuffer(datos, np.uint8), cv2.IMREAD_COLOR)
    if img is None:
        raise HTTPException(status_code=400, detail="El archivo no es una imagen valida.")

    alto, ancho = img.shape[:2]
    escala = min(1.0, MAX_SIDE / max(alto, ancho))
    if escala < 1.0:
        img = cv2.resize(img, (int(ancho * escala), int(alto * escala)), interpolation=cv2.INTER_AREA)
        alto, ancho = img.shape[:2]

    with lock:
        detector.setInputSize((ancho, alto))
        _, rostros = detector.detect(img)
        if rostros is None or len(rostros) == 0:
            raise HTTPException(status_code=422, detail="No se detecto ningun rostro. Mire a la camara con buena luz.")
        if len(rostros) > 1:
            raise HTTPException(status_code=422, detail="Se detecto mas de un rostro. Debe aparecer una sola persona.")
        rostro = rostros[0]
        if rostro[2] < MIN_FACE_PX:
            raise HTTPException(status_code=422, detail="El rostro se ve muy pequeno. Acerquese a la camara.")
        alineado = recognizer.alignCrop(img, rostro)
        vector = recognizer.feature(alineado)

    vector = vector.flatten().astype(np.float32)
    norma = float(np.linalg.norm(vector))
    if norma == 0.0:
        raise HTTPException(status_code=422, detail="No se pudo extraer el vector del rostro.")
    vector = vector / norma  # normalizado: el coseno es el producto punto

    return {
        "embedding": vector.tolist(),
        "modelo": "SFace-2021dec",
        "dimension": int(vector.shape[0]),
        "calidad": float(rostro[-1]),
    }
