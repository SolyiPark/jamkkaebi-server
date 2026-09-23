package com.example.jamkkaebi.purification.dto.response;

import com.example.jamkkaebi.artifact.domain.Difficulty;

/**
 * <b>이 도깨비에게</b> 난이도가 열려 있는지.
 * 수치는 {@code GET /api/difficulties} 가, 해금 여부는 도깨비마다 다르므로 여기서 답한다.
 */
public record DifficultyOption(Difficulty difficulty, boolean unlocked) {
}
