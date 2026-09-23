package com.example.jamkkaebi.purification.dto.response;

/**
 * 이번 판의 보드. <b>서버는 보드를 만들지 않고 시드만 준다</b> — 클라이언트가 같은 시드와 같은
 * 파라미터로 같은 보드를 만들어야 한다.
 */
public record BoardView(
        long seed,
        int width,
        int height,
        int threatCount,
        int shapeCount,
        int tapBudget,
        int scoutLimit
) {
}
