package com.example.jamkkaebi.artifact.repository;

import com.example.jamkkaebi.artifact.domain.ArtifactStatus;
import com.example.jamkkaebi.artifact.domain.UserArtifact;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

import java.util.Collection;
import java.util.List;
import java.util.Optional;

public interface UserArtifactRepository extends JpaRepository<UserArtifact, Long> {

    Optional<UserArtifact> findByUserIdAndArtifactId(Long userId, Integer artifactId);

    List<UserArtifact> findAllByUserId(Long userId);

    List<UserArtifact> findAllByUserIdAndStatus(Long userId, ArtifactStatus status);

    long countByUserIdAndStatus(Long userId, ArtifactStatus status);

    // 이미 보유한 유물 번호. 상자가 신규/중복 풀을 결정할 때 쓴다.
    @Query("SELECT ua.artifactId FROM UserArtifact ua WHERE ua.userId = :userId")
    List<Integer> findArtifactIdsByUserId(@Param("userId") Long userId);

    /**
     * 시대별 완료 유물 수
     *
     * <p>친구 목록의 수집률과 도감 진행률이 이 집계를 공유한다.
     */
    @Query("""
            SELECT new com.example.jamkkaebi.artifact.repository.EraCompletion(ua.userId, a.era, COUNT(ua))
              FROM UserArtifact ua
              JOIN ArtifactMaster a ON a.id = ua.artifactId
             WHERE ua.userId IN :userIds AND ua.status = com.example.jamkkaebi.artifact.domain.ArtifactStatus.COMPLETED
             GROUP BY ua.userId, a.era
            """)
    List<EraCompletion> countCompletedByEra(@Param("userIds") Collection<Long> userIds);
}
