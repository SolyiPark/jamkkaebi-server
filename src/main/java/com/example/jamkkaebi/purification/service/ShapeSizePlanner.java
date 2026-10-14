package com.example.jamkkaebi.purification.service;

import com.example.jamkkaebi.purification.config.PurificationProperties;
import org.springframework.stereotype.Component;

import java.util.ArrayList;
import java.util.List;

/**
 * 목표 형상의 칸 수 합을 형상마다 나눈다.
 *
 * <p>난이도는 <b>형상 개수와 칸 수 합</b>만 정한다.
 * 나누는 방법은 판마다 다르게 고르되 <b>보드 시드에서 결정</b>한다.
 *
 * 예) 3개/10칸이면 [5,3,2], [4,4,2], [4,3,3]
 */
@Component
public class ShapeSizePlanner {

    static final long SEED_SALT = 0x5348_4150_4553_495AL;

    private final PurificationProperties properties;

    public ShapeSizePlanner(PurificationProperties properties) {
        this.properties = properties;
    }

    /**
     * 이번 판의 형상별 칸 수.
     *
     * @return 내림차순. 길이는 {@code shapeCount}, 합은 {@code cellTotal}
     */
    public List<Integer> plan(long boardSeed, int shapeCount, int cellTotal) {
        List<List<Integer>> candidates = candidates(shapeCount, cellTotal);
        if (candidates.isEmpty()) {
            throw new IllegalArgumentException("형상 %d개로 %d칸을 나눌 수 없습니다.".formatted(shapeCount, cellTotal));
        }
        return candidates.get(new SplitMix64(boardSeed ^ SEED_SALT).nextInt(candidates.size()));
    }

    // 가능한 배분 전부
    List<List<Integer>> candidates(int shapeCount, int cellTotal) {
        List<List<Integer>> found = new ArrayList<>();
        collect(new ArrayList<>(), shapeCount, cellTotal, properties.shapeSizeMax(), found);
        return found;
    }

    private void collect(List<Integer> prefix, int shapesLeft, int cellsLeft, int ceiling,
                         List<List<Integer>> found) {
        if (shapesLeft == 0) {
            if (cellsLeft == 0) {
                found.add(List.copyOf(prefix));
            }
            return;
        }
        int min = properties.shapeSizeMin();
        int largest = Math.min(ceiling, cellsLeft - (shapesLeft - 1) * min);
        for (int size = largest; size >= min; size--) {
            if (size * shapesLeft < cellsLeft) {
                break;
            }
            prefix.add(size);
            collect(prefix, shapesLeft - 1, cellsLeft - size, size, found);
            prefix.removeLast();
        }
    }
}
