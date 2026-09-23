package com.example.jamkkaebi.artifact.domain;

/**
 * 봉인 매개체 4종. 보드 배경과 타일 텍스처, 강화 타일 이름을 결정한다.
 *
 * <p>타일 이름·파괴 감각 같은 연출 문구는 클라이언트 에셋이다.
 * 매개체는 유물의 실제 재질과 무관하게 배정되며, 한 시대의 유물 3종은 서로 다른 매개체를 쓴다.
 */
public enum SealMedium {
    RUST, // 굳은 녹 — 부스러진다
    FOG,  // 안개 — 흩어진다
    SOOT, // 그을음 — 묻어난다
    ROOT  // 뿌리 — 끊긴다
}
