package com.example.jamkkaebi.artifact.repository;

import com.example.jamkkaebi.artifact.domain.UserBuilding;
import org.springframework.data.jpa.repository.JpaRepository;

import java.util.List;
import java.util.Optional;

public interface UserBuildingRepository extends JpaRepository<UserBuilding, Long> {

    List<UserBuilding> findAllByUserId(Long userId);

    Optional<UserBuilding> findByUserIdAndBuildingId(Long userId, Integer buildingId);

    boolean existsByUserIdAndBuildingId(Long userId, Integer buildingId);
}
